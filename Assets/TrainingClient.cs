using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// Socket client that communicates with the Python 3DGS training server.
/// Handles connection, message send/receive, and thread-safe main thread dispatch.
/// </summary>
public class TrainingClient : MonoBehaviour
{
    [Header("Server Settings")]
    public string host = "127.0.0.1";
    public int port = 9090;

    [Header("References")]
    public GaussianSplatRenderer splatRenderer;

    // Connection state
    private TcpClient _client;
    private NetworkStream _stream;
    private Thread _recvThread;
    private volatile bool _connected;

    // Main thread callback queue
    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

    // Training state
    public enum TrainingState { Idle, Connecting, Connected, Training, Complete, Error }
    public TrainingState State { get; private set; } = TrainingState.Idle;
    public string StatusMessage { get; private set; } = "";
    public string LastPlyPath { get; private set; } = "";

    // Progress tracking
    public float Progress { get; private set; } = 0f;       // 0-100
    public int CurrentIteration { get; private set; } = 0;
    public int TotalIterations { get; private set; } = 0;
    public float CurrentLoss { get; private set; } = 0f;
    public string ProgressInfo { get; private set; } = "";

    // Events
    public event Action<string> OnTrainingComplete; // ply_path
    public event Action<string> OnError;            // error message
    public event Action<float> OnTrainingProgress;  // progress 0-100

    void Start()
    {
        if (splatRenderer == null)
            splatRenderer = FindObjectOfType<GaussianSplatRenderer>();
    }

    void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
            action.Invoke();
    }

    /// <summary>
    /// Connect to the Python training server.
    /// </summary>
    public void Connect()
    {
        if (_connected) return;

        State = TrainingState.Connecting;
        StatusMessage = $"Connecting to {host}:{port}...";

        try
        {
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
            _connected = true;

            _recvThread = new Thread(ReceiveLoop) { IsBackground = true };
            _recvThread.Start();

            State = TrainingState.Connected;
            StatusMessage = "Connected";
            Debug.Log($"[TrainingClient] Connected to {host}:{port}");
        }
        catch (Exception e)
        {
            State = TrainingState.Error;
            StatusMessage = $"Connection failed: {e.Message}";
            Debug.LogError($"[TrainingClient] {StatusMessage}");
        }
    }

    /// <summary>
    /// Disconnect from the server.
    /// </summary>
    public void Disconnect()
    {
        _connected = false;
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
        State = TrainingState.Idle;
        StatusMessage = "Disconnected";
        Debug.Log("[TrainingClient] Disconnected");
    }

    /// <summary>
    /// Send a start training request to the Python server.
    /// </summary>
    /// <param name="dataPath">Path to COLMAP data directory</param>
    /// <param name="outputDir">Output directory for trained model</param>
    /// <param name="mock">Use mock training (for testing)</param>
    /// <param name="iterations">Number of training iterations</param>
    public void StartTraining(string dataPath, string outputDir = "", bool mock = true, int iterations = 7000)
    {
        if (!_connected)
        {
            Connect();
            if (!_connected) return;
        }

        State = TrainingState.Training;
        Progress = 0f;
        CurrentIteration = 0;
        TotalIterations = iterations;
        CurrentLoss = 0f;
        ProgressInfo = "";
        StatusMessage = mock ? "Mock training..." : $"Training ({iterations} iters)...";

        var msg = new TrainingRequest
        {
            type = "start_training",
            data_path = dataPath,
            output_dir = outputDir,
            mock = mock,
            iterations = iterations
        };

        string json = JsonUtility.ToJson(msg);
        SendRawMessage(json);
        Debug.Log($"[TrainingClient] Sent start_training: mock={mock}");
    }

    /// <summary>
    /// Send a stop training request to the Python server.
    /// </summary>
    public void StopTraining()
    {
        if (!_connected || State != TrainingState.Training) return;

        SendRawMessage("{\"type\":\"stop_training\"}");
        StatusMessage = "Stopping...";
        Debug.Log("[TrainingClient] Sent stop_training");
    }

    // --- Internal message handling ---

    private void SendRawMessage(string json)
    {
        if (!_connected || _stream == null) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] header = BitConverter.GetBytes((uint)data.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(header);
            _stream.Write(header, 0, 4);
            _stream.Write(data, 0, data.Length);
            _stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TrainingClient] Send error: {e.Message}");
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (_connected)
            {
                byte[] headerBuf = ReadExact(4);
                if (headerBuf == null) break;
                if (BitConverter.IsLittleEndian) Array.Reverse(headerBuf);
                int len = (int)BitConverter.ToUInt32(headerBuf, 0);

                byte[] body = ReadExact(len);
                if (body == null) break;

                string json = Encoding.UTF8.GetString(body);
                Debug.Log($"[TrainingClient] Received: {json}");

                // Parse on main thread
                _mainThreadActions.Enqueue(() => HandleMessage(json));
            }
        }
        catch (Exception e)
        {
            if (_connected)
            {
                _mainThreadActions.Enqueue(() =>
                {
                    State = TrainingState.Error;
                    StatusMessage = $"Receive error: {e.Message}";
                    Debug.LogError($"[TrainingClient] {StatusMessage}");
                });
            }
        }
    }

    private void HandleMessage(string json)
    {
        // Simple JSON parsing (Unity's JsonUtility needs known types)
        var response = JsonUtility.FromJson<ServerResponse>(json);

        switch (response.type)
        {
            case "training_started":
                Progress = 0f;
                CurrentIteration = 0;
                CurrentLoss = 0f;
                ProgressInfo = "";
                StatusMessage = "Training in progress...";
                break;

            case "training_progress":
                Progress = response.progress;
                CurrentIteration = response.iteration;
                TotalIterations = response.total_iterations;
                if (response.loss > 0f) CurrentLoss = response.loss;
                if (!string.IsNullOrEmpty(response.info)) ProgressInfo = response.info;
                StatusMessage = $"Training: {response.progress:F1}% ({response.iteration}/{response.total_iterations})";
                OnTrainingProgress?.Invoke(response.progress);
                break;

            case "training_stopped":
                State = TrainingState.Connected;
                Progress = 0f;
                StatusMessage = "Training stopped";
                Debug.Log("[TrainingClient] Training stopped by user");
                break;

            case "training_complete":
                if (response.status == "ok")
                {
                    LastPlyPath = response.ply_path;
                    State = TrainingState.Complete;
                    StatusMessage = $"Done: {System.IO.Path.GetFileName(response.ply_path)}";
                    Debug.Log($"[TrainingClient] Training complete: {response.ply_path}");
                    OnTrainingComplete?.Invoke(response.ply_path);
                }
                else
                {
                    State = TrainingState.Error;
                    StatusMessage = $"Training failed: {response.message}";
                    Debug.LogError($"[TrainingClient] {StatusMessage}");
                    OnError?.Invoke(response.message);
                }
                break;

            case "pong":
                Debug.Log("[TrainingClient] Pong received");
                break;

            case "error":
                State = TrainingState.Error;
                StatusMessage = $"Server error: {response.message}";
                OnError?.Invoke(response.message);
                break;
        }
    }

    private byte[] ReadExact(int count)
    {
        byte[] buf = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = _stream.Read(buf, offset, count - offset);
            if (read == 0) return null;
            offset += read;
        }
        return buf;
    }

    void OnDestroy()
    {
        Disconnect();
    }

    // --- JSON message types ---

    [Serializable]
    private class TrainingRequest
    {
        public string type;
        public string data_path;
        public string output_dir;
        public bool mock;
        public int iterations;
    }

    [Serializable]
    private class ServerResponse
    {
        public string type;
        public string status;
        public string ply_path;
        public string message;
        // Progress fields
        public int iteration;
        public int total_iterations;
        public float progress;
        public float loss;
        public string info;
    }
}
