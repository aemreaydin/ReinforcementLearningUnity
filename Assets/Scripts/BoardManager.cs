using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using Random = UnityEngine.Random;

internal struct Hyperparams
{
    public readonly int TotalSimulations;
    public readonly int StepsBeforeDeath;
    public readonly float LearningRate;
    public readonly float DiscountRate;

    public float ExplorationRate;
    public readonly float MaxExplorationRate;
    public readonly float MinExplorationRate;
    public readonly float ExplorationDecayRate;

    public Hyperparams(
        int totalSimulations = 20000,
        int stepsBeforeDeath = 100,
        float learningRate = 0.7f,
        float discountRate = 0.95f,
        float explorationRate = 1.0f,
        float maxExplorationRate = 1.0f,
        float minExplorationRate = 0.01f,
        float explorationDecayRate = 0.005f)
    {
        TotalSimulations = totalSimulations;
        StepsBeforeDeath = stepsBeforeDeath;
        LearningRate = learningRate;
        DiscountRate = discountRate;
        ExplorationRate = explorationRate;
        MaxExplorationRate = maxExplorationRate;
        MinExplorationRate = minExplorationRate;
        ExplorationDecayRate = explorationDecayRate;
    }
}

public class BoardManager : MonoBehaviour
{
    public GameObject tilePrefab;
    public int boardRows;
    public int boardCols;

    public Vector2Int startPoint;
    public Vector2Int endPoint;
    public Vector2Int[] deathPoints;


    [UsedImplicitly] private Camera _camera;
    private GameObject[] _tiles;
    

    /*
        Actions:
        Right -> 0
        Down -> 1
        Left -> 2
        Up -> 3
    */
    private TileState[] _tileStates;
    private float[] _tileRewards;

    private int _currentTileIndex;
    private int _currentState;
    private float _currentReward;
    private ActionState _currentAction;

    private int _actionSize;
    private int _stateSize;
    private float[][] _qTable;
    private int _state;
    private Hyperparams _params;
    private readonly List<float> _rewards = new List<float>();

    // Start is called before the first frame update
    private void Start()
    {
        _camera = Camera.main;

        InitializeTiles();
        InitializeBoardVariables();

        RunTraining();

        StartCoroutine(nameof(RunTest));
    }

    // Update is called once per frame

    private void InitializeBoardVariables()
    {
        _actionSize = 4;
        _stateSize = boardCols * boardRows;
        _qTable = new float[_stateSize][];
        for (var i = 0; i != _qTable.Length; i++)
            _qTable[i] = new float[_actionSize];
        // Initialize rewards based on tiles
        _tileRewards = new float[_stateSize];
        _tileStates = new TileState[_stateSize];
        for (var i = 0; i != _tiles.Length; i++)
        {
            var tile = _tiles[i].GetComponent<Tile>();
            _tileStates[i] = tile.state;
            switch (tile.state)
            {
                case TileState.Empty:
                case TileState.Start:
                    _tileRewards[i] = -1.0f;
                    break;
                case TileState.Destination:
                    _tileRewards[i] = 100.0f;
                    break;
                case TileState.Death:
                    _tileRewards[i] = -100.0f;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        _params = new Hyperparams(10000);
    }

    private void InitializeTiles()
    {
        _tiles = new GameObject[boardRows * boardCols];

        for (var row = 0; row != boardRows; row++)
        for (var col = 0; col != boardCols; col++)
        {
            var tile = Instantiate(tilePrefab, new Vector3(row, col, 0), Quaternion.identity);
            tile.GetComponent<Tile>().state = TileState.Empty;
            _tiles[row * boardRows + col] = tile;
        }

        var start = _tiles[startPoint.x * boardRows + startPoint.y];
        start.GetComponent<Tile>().state = TileState.Start;
        var startRenderer = start.GetComponent<Renderer>();
        startRenderer.material.color = Color.yellow;

        var end = _tiles[endPoint.x * boardRows + endPoint.y];
        end.GetComponent<Tile>().state = TileState.Destination;
        var endRenderer = end.GetComponent<Renderer>();
        endRenderer.material.color = Color.green;

        for (var i = 0; i != deathPoints.Length; i++)
        {
            var death = _tiles[deathPoints[i].x * boardRows + deathPoints[i].y];
            death.GetComponent<Tile>().state = TileState.Death;
            var deathRenderer = death.GetComponent<Renderer>();
            deathRenderer.material.color = Color.red;
        }

        _currentTileIndex = startPoint.x * boardRows + startPoint.y;
    }

    private int FindBestActionInStateIndex(int state)
    {
        return _qTable[state].ToList().IndexOf(_qTable[state].Max());
    }
    
    private float FindBestActionInState(int state)
    {
        return _qTable[state].Max();
    }

    private bool Step(ActionState actionState)
    {
        switch (actionState)
        {
            case ActionState.Right:
                _currentState = (_state + 1) % _stateSize != 0 ? _state + 1 : _state;
                break;
            case ActionState.Down:
                _currentState = _state < boardCols * (boardRows - 1) ? _state + boardCols : _state;
                break;
            case ActionState.Left:
                _currentState = _state % boardRows != 0 ? _state - 1 : _state;
                break;
            case ActionState.Up:
                _currentState = _state > (boardCols - 1) ? _state - boardCols : _state;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return _tileStates[_currentState] == TileState.Death || _tileStates[_currentState] == TileState.Destination;
    }

    private void RunTraining()
    {
        for (var totalIter = 0; totalIter != _params.TotalSimulations; totalIter++)
        {
            _currentState = 0;
            _state = 0;
            var totalRewards = 0.0f;

            for (var iter = 0; iter != _params.StepsBeforeDeath; iter++)
            {
                bool done;
                // Decide if exploration or exploitation
                var rand = Random.Range(0.0f, 1.0f);
                // If random number is bigger than epsilon do exploitation, otherwise do exploration
                /*
                Actions:
                Right -> 0
                Down -> 1
                Left -> 2
                Up -> 3
                */
                var action = rand > _params.ExplorationRate ? FindBestActionInStateIndex(_state) : Random.Range(0, 4);
                done = Step((ActionState) action);

                _currentReward = _tileRewards[_currentState];
                // Update Q-Table
                _qTable[_state][action] +=
                    _params.LearningRate *
                    (_currentReward + _params.DiscountRate * FindBestActionInState(_currentState)) -
                    _qTable[_state][action];

                totalRewards += _currentReward;
                _state = _currentState;

                if (done) break;
            }

            _params.ExplorationRate = _params.MinExplorationRate +
                                      (_params.MaxExplorationRate - _params.MinExplorationRate) *
                                      Mathf.Exp(-_params.ExplorationDecayRate * totalIter);
            _rewards.Add(totalRewards);
            
            // Debug.Log(
            //     $"Score Over Time: {(_rewards.Sum() / totalIter + 1).ToString(CultureInfo.CurrentCulture)}");
        }

        Debug.Log(
            $"Score Over Time: {(_rewards.Sum() / _params.TotalSimulations).ToString(CultureInfo.CurrentCulture)}");
        Debug.Log(_qTable);
    }

    private IEnumerator RunTest()
    {
        for (var totalIter = 0; totalIter != 5; totalIter++)
        {
            _currentState = 0;
            _state = 0;
            
            for (var iter = 0; iter != _params.StepsBeforeDeath; iter++)
            {
                // Decide if exploration or exploitation
                var rand = Random.Range(0.0f, 1.0f);
                // If random number is bigger than epsilon do exploitation, otherwise do exploration
                var action = rand > _params.ExplorationRate ? FindBestActionInStateIndex(_state) : Random.Range(0, 4);
                var done = Step((ActionState) action);

                _state = _currentState;
                Debug.Log(_state.ToString());

                _tiles[_state].GetComponent<Renderer>().material.color = Color.yellow;

                if (done)
                {
                    StopCoroutine(nameof(RunTest));
                }
                yield return new WaitForSeconds(0.5f);
            }
        }
    }
    // private void Play()
    // {
    //     if (!Input.GetMouseButtonDown(0)) return;
    //
    //     var ray = _camera.ScreenPointToRay(Input.mousePosition);
    //     if (!Physics.Raycast(ray, out var rayHit)) return;
    //     if (!rayHit.collider.gameObject.CompareTag("Tile")) return;
    //
    //     var hitObject = rayHit.collider.gameObject;
    //     var hitTile = hitObject.GetComponent<Tile>();
    //     if (hitTile.state != TileState.Empty) return;
    //
    //     var mat = hitObject.GetComponent<Renderer>();
    //     hitTile.state = _oTurn ? TileState.O : TileState.X;
    //     mat.material.color = _oTurn ? Color.red : Color.blue;
    //
    //     _oTurn = !_oTurn;
    // }
}