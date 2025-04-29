using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

namespace jeanf.tooltip
{
    [RequireComponent(typeof(LineRenderer))]
    [RequireComponent(typeof(ObjectPool<>))]
    public class NavigationToolTip : ToolTip
    {
        [SerializeField] private Transform target;
        [SerializeField] private NavMeshSurface navMeshSurface;
        
        [Header("General Settings")]
        [Tooltip("Threshold at which the player is arrived at destination")]
        [SerializeField] private float destinationThreshold = 1f;
        [Header("Line Settings")]
        [SerializeField] private Color startColor = Color.yellow;
        [SerializeField] private Color endColor = Color.yellow;
        [SerializeField] private Material material;
        [SerializeField] private float lineWidth = 0.05f;
        [Header("Sprite Settings")]
        [SerializeField] private float spacing = 1f;
        [Tooltip("Distance from which player is considered too far from closest sprite")]
        [SerializeField] private float playerDistanceThresholdFirstSprite = 3f;
        [Tooltip("Distance from which player is considered too far from sprite path")]
        [SerializeField] private float playerDistanceThresholdSpritePath = 1.5f;
        [Header("Display Choice")]
        [SerializeField] private NavigationToolTipType navigationToolTipType;
        [Header("Map Display Settings")]
        [SerializeField] private Transform topLeft;
        [SerializeField] private Transform topRight;
        [SerializeField] private Transform bottomLeft;
        [SerializeField] private Transform bottomRight;
        
        public delegate void UpdateNormalisedPathDelegate(List<Vector2> normalisedPath);
        public static UpdateNormalisedPathDelegate UpdateNormalisedPath;
        
        private List<Vector3> worldPath;
        private List<Vector2> _normalisedPath;

        private NavMeshPath _path;
        private Vector3 _playerNavMeshPosition;
        private Vector3 _targetNavMeshPosition;

        private bool isPlayerOnPath = false;
        
        private LineRenderer _lineRenderer;
        private List<GameObject> sprites;
        private ObjectPool _objectPool;

        private const int SPRITE_ROTATION_X = -90;
        private const int SPRITE_ROTATION_Y = 180;
        private const int SPRITE_ROTATION_Z = 0;
        private Transform _playerTransform;

        private Vector3 _lastPlayerPosition;
        
        private void Awake()
        {
            worldPath = new List<Vector3>();
            _normalisedPath = new List<Vector2>();
            _path = new NavMeshPath();
            _lineRenderer = GetComponent<LineRenderer>();
            _objectPool = GetComponent<ObjectPool>();
            
            _playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
            _lastPlayerPosition = _playerTransform.position;
            
            sprites = new List<GameObject>();

            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = lineWidth;
            _lineRenderer.material = material;
            _lineRenderer.startColor = startColor;
            _lineRenderer.endColor = endColor;
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            NavigationDestinationSender.OnSendDestination += SetDestination;
            OnUpdateIsShowingToolTip += UpdateIsShowingToolTip;
        }

        private void Unsubscribe()
        {
            NavigationDestinationSender.OnSendDestination -= SetDestination;
            OnUpdateIsShowingToolTip -= UpdateIsShowingToolTip;
        }

        private void Update()
        {
            if (!showToolTip) { HideLine(); HideSprites(); return; }
            if (_playerTransform is null || target is null || navMeshSurface is null) return;
            
            if (PlayerArrivedToDestination())
            {
                showToolTip = false;
                return;
            }
            
            if (IsPathFound())
            {
                switch (navigationToolTipType)
                {
                    case NavigationToolTipType.LineRenderer:
                        HideSprites();
                        DrawLine();
                        break;
                    case NavigationToolTipType.SpriteLine:
                        HideLine();
                        DrawSpritesPath();
                        break;
                }
            }
            else
            {
                switch (navigationToolTipType)
                {
                    case NavigationToolTipType.LineRenderer:
                        HideLine();
                        break;
                    case NavigationToolTipType.SpriteLine:
                        HideSprites();
                        break;
                }
            }
            
            _lastPlayerPosition = _playerTransform.position;
        }

        private bool IsPathFound()
        {
            _playerNavMeshPosition = GetNearestNavMeshPoint(_playerTransform.position);
            _targetNavMeshPosition = GetNearestNavMeshPoint(target.position);

            return (NavMesh.CalculatePath(_playerNavMeshPosition, 
                                            _targetNavMeshPosition, 
                                            NavMesh.AllAreas, _path) &&
                    _path.corners.Length > 0);
        }

        private void UpdatePath()
        {
            _playerNavMeshPosition = GetNearestNavMeshPoint(_playerTransform.position);
            _targetNavMeshPosition = GetNearestNavMeshPoint(target.position);

            NavMesh.CalculatePath(_playerNavMeshPosition,
                _targetNavMeshPosition,
                NavMesh.AllAreas, _path);
        }
        
        Vector3 GetNearestNavMeshPoint(Vector3 position)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, 10.0f, NavMesh.AllAreas))
            {
                return hit.position;
            }
            return position;
        }

        private void DrawLine()
        {
            Vector3[] elevatedPath = new Vector3[_path.corners.Length];
            for (int i = 0; i < _path.corners.Length; i++)
            {
                elevatedPath[i] = _path.corners[i] + Vector3.up * 0.1f;
            }

            _lineRenderer.positionCount = elevatedPath.Length;
            _lineRenderer.SetPositions(elevatedPath);
        }

        private void HideLine()
        {
            _lineRenderer.positionCount = 0;
        }
        
        private void DrawSpritesPath()
        {
            if (_path.corners.Length == 0) return;
            
            if (IsPlayerGoingTowardPath())
            {
                CheckAndRemoveFirstSprite();
                _lastPlayerPosition = _playerTransform.position;
                isPlayerOnPath = true;
                return;
            }

            if (!IsPlayerNearFirstSprite())
            {
                DrawSpritesFromZero();
                isPlayerOnPath = false;
                return;
            }

            if (!IsPlayerOnSpritePath() && !IsPlayerInMovement())
            {
                DrawSpritesFromZero();
            }
            
        }

        private bool PlayerArrivedToDestination()
        {
            return (Vector3.Distance(_playerTransform.position, target.position) <= destinationThreshold);
        }

        private void CheckAndRemoveFirstSprite()
        {
            if (sprites.Count == 0) return;

            GameObject firstSprite = sprites[0];

            if (Vector3.Distance(_playerTransform.position, firstSprite.transform.position) < spacing * 0.5f)
            {
                _objectPool.Release(firstSprite);
                sprites.RemoveAt(0);
                
                if(worldPath.Count > 0) worldPath.RemoveAt(0);
                
                NormalisePath();
                UpdateNormalisedPath?.Invoke(_normalisedPath);
            }
        }

        private bool IsPlayerGoingTowardPath()
        {
            if (sprites.Count > 0)
            {
                GameObject nextSprite = sprites[0];
                Vector3 toNextSprite = (nextSprite.transform.position - _playerTransform.position).normalized;
                Vector3 playerMovement = (_playerTransform.position - _lastPlayerPosition).normalized;

                float movementSimilarity = Vector3.Dot(playerMovement, toNextSprite);

                return Vector3.Distance(_playerTransform.position, _lastPlayerPosition) > 0.01f && movementSimilarity > 0.5f;
            }
            return false;
        }

        private bool IsPlayerNearFirstSprite()
        {
            if (sprites.Count == 0 || _playerTransform is null) return false;
                
            GameObject firstSprite = sprites[0];
            float distance = Vector3.Distance(_playerTransform.position, firstSprite.transform.position);

            return distance < playerDistanceThresholdFirstSprite;
        }
        
        private bool IsPlayerInMovement()
        {
            float movementThreshold = 0.005f;
            
            if (_playerTransform is null) return false;
            
            float distanceMoved = Vector3.Distance(_playerTransform.position, _lastPlayerPosition);

            return distanceMoved > movementThreshold;
        }

        private bool IsPlayerOnSpritePath()
        {
            if (sprites.Count == 0 || _playerTransform is null)
                return false;

            for (int i = 0; i < sprites.Count; i++)
            {
                if (Vector3.Distance(_playerTransform.position, sprites[i].transform.position) < playerDistanceThresholdSpritePath)
                {
                    return true;
                }
            }

            return false;
        }
        
        private void DrawSpritesFromZero()
        {
            HideSprites();
    
            float distanceCovered = 0f;
            
            worldPath.Clear();

            for (int i = 1; i < _path.corners.Length; i++)
            {
                Vector3 start = _path.corners[i - 1];
                Vector3 end = _path.corners[i];

                float segmentLength = Vector3.Distance(start, end);

                while (distanceCovered + spacing <= segmentLength)
                {
                    distanceCovered += spacing;
                    float relativePosition = distanceCovered / segmentLength;
                    Vector3 position = Vector3.Lerp(start, end, relativePosition);

                    GameObject sprite = _objectPool.Get(position);
                    
                    if(sprite is not null)
                        sprites.Add(sprite);

                    worldPath.Add(position);
                }

                distanceCovered -= segmentLength;
            }

            NormalisePath();
            UpdateNormalisedPath?.Invoke(_normalisedPath);
            
        }

        private void HideSprites()
        {
            int count = transform.childCount;
    
            for (int i = count - 1; i >= 0; i--)
            {
                GameObject sprite = transform.GetChild(i).gameObject;
                if(sprite.activeSelf)
                    _objectPool.Release(transform.GetChild(i).gameObject);
            }
            
            sprites.Clear();
        }
        
        private void NormalisePath()
        {
            if (_path.corners is null || _path.corners.Length == 0) return;
            if (topLeft is null || topRight is null || bottomRight is null || bottomLeft is null) return;

            float width = topLeft.position.x - topRight.position.x;
            float height = topLeft.position.z - bottomLeft.position.z;

            List<Vector2> result = new List<Vector2>();
            for (int i = 0; i < _path.corners.Length; i++)
            {
                Vector3 pos = _path.corners[i];
                
                float distanceFromTopLeftX = (topLeft.position.x - pos.x) / width;
                float distanceFromTopLeftY = 1 - (1 - (topLeft.position.z - pos.z) / height);
                
                result.Add(new Vector2(distanceFromTopLeftX, distanceFromTopLeftY));
            }
            
            _normalisedPath = result;
        }

        private void SetDestination(Transform targetTransform)
        {
            target = targetTransform;
            showToolTip = true;
            UpdatePath();
            DrawSpritesFromZero();
        }
    }
}