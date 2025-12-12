using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

namespace jeanf.tooltip
{
    [RequireComponent(typeof(LineRenderer))]
    [RequireComponent(typeof(ObjectPool<GameObject>))]
    public class NavigationToolTip : ToolTip
    {
        [SerializeField] private Transform target;
        [SerializeField] private NavMeshSurface navMeshSurface;
        
        [Header("General Settings")]
        [Tooltip("Threshold at which the player is arrived at destination")]
        [SerializeField] private float destinationThreshold = 1f;
        [Tooltip("Distance at which the player and the target is considered on navmesh even if not on it")]
        [SerializeField] private float navmeshDetectionDistance = 10f;
        [Tooltip("Time interval in seconds between path recalculations (0 = every frame)")]
        [SerializeField] private float pathSamplingRate = 0.2f;
        [Header("Line Settings")]
        [SerializeField] private Color startColor = Color.yellow;
        [SerializeField] private Color endColor = Color.yellow;
        [SerializeField] private Material material;
        [SerializeField] private float lineWidth = 0.05f;
        [Header("Sprite Settings")]
        [SerializeField] private float spacing = 1f;
        [SerializeField] private bool changeLastSpriteColor = true;
        [SerializeField] private Color lastSpriteColor = Color.yellow;
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
        
        public delegate void BroadcastPathDelegate(NavMeshPath path);
        public static BroadcastPathDelegate OnBroadcastPath;
        
        private List<Vector3> _worldPath;
        private float[][] _normalisedPath;

        private NavMeshPath _path;
        private Vector3 _playerNavMeshPosition;
        private Vector3 _targetNavMeshPosition;

        private bool _isPlayerOnPath = false;
        
        private LineRenderer _lineRenderer;
        private List<GameObject> _sprites;
        private NavigationObjectPool _navigationObjectPool;
        
        private Transform _playerTransform;

        private Vector3 _lastPlayerPosition;
        
        //Timer for path sampling rate
        private float _pathUpdateTimer = 0f;
        
        //Cache to avoid allocations in DrawLine
        private Vector3[] _elevatedPathCache;
        
        //Cache for NavMeshHit (avoids boxing)
        private NavMeshHit _navMeshHit;
        
        //Performance optimization: track if path/visuals need update
        private bool _pathNeedsRedraw = false;
        private int _lastPathCornerCount = 0;
        
        //Constants to avoid recalculations
        private const float MOVEMENT_THRESHOLD = 0.005f;
        private const float MIN_MOVEMENT_DETECTION = 0.01f;
        private const float DOT_PRODUCT_THRESHOLD = 0.5f;
        private const float SPRITE_REMOVAL_MULTIPLIER = 0.5f;
        private const float ELEVATION_OFFSET = 0.1f;
        
        //Cache for squared distance calculations (faster than Distance)
        private float _destinationThresholdSqr;
        private float _playerDistanceThresholdFirstSpriteSqr;
        private float _playerDistanceThresholdSpritePathSqr;
        private float _spacingSqr;
        private float _movementThresholdSqr;
        private float _minMovementDetectionSqr;
        
        private void Awake()
        {
            _worldPath = new List<Vector3>(128);
            _normalisedPath = null;
            _path = new NavMeshPath();
            _lineRenderer = GetComponent<LineRenderer>();
            _navigationObjectPool = GetComponent<NavigationObjectPool>();
            
            _playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
            _lastPlayerPosition = _playerTransform.position;
            
            _sprites = new List<GameObject>(128);
            _elevatedPathCache = new Vector3[128];

            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = lineWidth;
            _lineRenderer.material = material;
            _lineRenderer.startColor = startColor;
            _lineRenderer.endColor = endColor;
            
            _lineRenderer.enabled = false;
            
            CacheSquaredDistances();
        }

        private void CacheSquaredDistances()
        {
            _destinationThresholdSqr = destinationThreshold * destinationThreshold;
            _playerDistanceThresholdFirstSpriteSqr = playerDistanceThresholdFirstSprite * playerDistanceThresholdFirstSprite;
            _playerDistanceThresholdSpritePathSqr = playerDistanceThresholdSpritePath * playerDistanceThresholdSpritePath;
            _spacingSqr = spacing * spacing;
            _movementThresholdSqr = MOVEMENT_THRESHOLD * MOVEMENT_THRESHOLD;
            _minMovementDetectionSqr = MIN_MOVEMENT_DETECTION * MIN_MOVEMENT_DETECTION;
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            NavigationDestinationSender.OnSendDestination += SetDestination;
            NavigationMapCornerSender.OnSendNewMapCorner += SetNewMapCorner;
        }

        private void Unsubscribe()
        {
            NavigationDestinationSender.OnSendDestination -= SetDestination;
            NavigationMapCornerSender.OnSendNewMapCorner -= SetNewMapCorner;
        }

        private void Update()
        {
            if (!showToolTip) { HideLine(); HideSprites(); return; }
            if (_playerTransform == null || target == null) return;
            
            if (PlayerArrivedToDestination())
            {
                showToolTip = false;
                return;
            }
            
            _pathUpdateTimer += Time.deltaTime;
            
            bool shouldUpdatePath = pathSamplingRate <= 0f || _pathUpdateTimer >= pathSamplingRate;
            
            if (shouldUpdatePath)
            {
                _pathUpdateTimer = 0f;
                
                if (UpdatePath())
                {
                    bool pathChanged = _path.corners.Length != _lastPathCornerCount;
                    _lastPathCornerCount = _path.corners.Length;
                    
                    if (pathChanged)
                    {
                        _pathNeedsRedraw = true;
                    }
                }
                else
                {
                    if (navigationToolTipType == NavigationToolTipType.LineRenderer)
                        HideLine();
                    else
                        HideSprites();
                    return;
                }
            }
            
            if (navigationToolTipType == NavigationToolTipType.LineRenderer)
            {
                UpdateLineRenderer();
            }
            else
            {
                UpdateSpriteLine();
            }
            
            _lastPlayerPosition = _playerTransform.position;
        }

        private void UpdateLineRenderer()
        {
            HideSprites();
            
            if (_pathNeedsRedraw)
            {
                DrawLine();
                _pathNeedsRedraw = false;
            }
        }

        private void UpdateSpriteLine()
        {
            HideLine();
            
            if (_path.corners.Length == 0) return;
            
            DrawSpritesPath();
        }

        private bool UpdatePath()
        {
            _playerNavMeshPosition = GetNearestNavMeshPoint(_playerTransform.position);
            _targetNavMeshPosition = GetNearestNavMeshPoint(target.position);
            
            var isPath = NavMesh.CalculatePath(_playerNavMeshPosition, 
                             _targetNavMeshPosition, 
                             NavMesh.AllAreas, _path) &&
                         _path.corners.Length > 0;

            if(isPath) OnBroadcastPath?.Invoke(_path);
            
            return isPath;
        }
        
        Vector3 GetNearestNavMeshPoint(Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out _navMeshHit, navmeshDetectionDistance, NavMesh.AllAreas))
            {
                return _navMeshHit.position;
            }
            return position;
        }

        private void DrawLine()
        {
            _lineRenderer.enabled = true;
            
            int cornerCount = _path.corners.Length;
            
            if (_elevatedPathCache.Length < cornerCount)
            {
                _elevatedPathCache = new Vector3[cornerCount * 2]; 
            }
            
            for (int i = 0; i < cornerCount; i++)
            {
                Vector3 corner = _path.corners[i];
                corner.y += ELEVATION_OFFSET;
                _elevatedPathCache[i] = corner;
            }
            
            _lineRenderer.positionCount = cornerCount;
            _lineRenderer.SetPositions(_elevatedPathCache);
        }

        private void HideLine()
        {
            if(_lineRenderer.enabled)
                _lineRenderer.enabled = false;
        }

        private void DrawSpritesPath()
        {
            if (_path.corners.Length == 0) return;
            
            if (IsPlayerGoingTowardPath())
            {
                CheckAndRemoveFirstSprite();
                _isPlayerOnPath = true;
                return;
            }

            if (!IsPlayerNearFirstSprite())
            {
                DrawSpritesFromZero();
                _isPlayerOnPath = false;
                return;
            }

            if (!IsPlayerOnSpritePath() && !IsPlayerInMovement())
            {
                DrawSpritesFromZero();
            }
        }
        
        private bool PlayerArrivedToDestination()
        {
            if(target == null) return true;
            if(_playerTransform == null) return true;
            Vector3 delta = _playerTransform.position - target.position;
            return delta.sqrMagnitude <= _destinationThresholdSqr;
        }

        private void CheckAndRemoveFirstSprite()
        {
            if (_sprites.Count == 0) return;

            GameObject firstSprite = _sprites[0];

            Vector3 delta = _playerTransform.position - firstSprite.transform.position;
            float spacingThresholdSqr = _spacingSqr * (SPRITE_REMOVAL_MULTIPLIER * SPRITE_REMOVAL_MULTIPLIER);
            
            if (delta.sqrMagnitude < spacingThresholdSqr)
            {
                _navigationObjectPool.Release(firstSprite);
                _sprites.RemoveAt(0);
                
                if(_worldPath.Count > 0) _worldPath.RemoveAt(0);
            }
        }

        private bool IsPlayerGoingTowardPath()
        {
            if (_sprites.Count > 0)
            {
                GameObject nextSprite = _sprites[0];
                
                Vector3 toNextSprite = nextSprite.transform.position - _playerTransform.position;
                Vector3 playerMovement = _playerTransform.position - _lastPlayerPosition;
                
                float movementSqrMag = playerMovement.sqrMagnitude;
                if (movementSqrMag <= _minMovementDetectionSqr) 
                    return false;
                
                float toNextSpriteMag = toNextSprite.magnitude;
                float playerMovementMag = Mathf.Sqrt(movementSqrMag);
                
                if (toNextSpriteMag < 0.0001f || playerMovementMag < 0.0001f)
                    return false;
                
                toNextSprite /= toNextSpriteMag;
                playerMovement /= playerMovementMag;

                float movementSimilarity = Vector3.Dot(playerMovement, toNextSprite);

                return movementSimilarity > DOT_PRODUCT_THRESHOLD;
            }
            return false;
        }

        private bool IsPlayerNearFirstSprite()
        {
            if (_sprites.Count == 0 || _playerTransform is null) return false;
                
            GameObject firstSprite = _sprites[0];
            Vector3 delta = _playerTransform.position - firstSprite.transform.position;
            return delta.sqrMagnitude < _playerDistanceThresholdFirstSpriteSqr;
        }
        
        private bool IsPlayerInMovement()
        {
            if (_playerTransform is null) return false;
            
            Vector3 delta = _playerTransform.position - _lastPlayerPosition;
            return delta.sqrMagnitude > _movementThresholdSqr;
        }

        private bool IsPlayerOnSpritePath()
        {
            if (_sprites.Count == 0 || _playerTransform is null)
                return false;

            Vector3 playerPos = _playerTransform.position;
            
            int checkCount = Mathf.Min(_sprites.Count, 5);
            for (int i = 0; i < checkCount; i++)
            {
                Vector3 delta = playerPos - _sprites[i].transform.position;
                if (delta.sqrMagnitude < _playerDistanceThresholdSpritePathSqr)
                {
                    return true;
                }
            }
            
            for (int i = checkCount; i < _sprites.Count; i++)
            {
                Vector3 delta = playerPos - _sprites[i].transform.position;
                if (delta.sqrMagnitude < _playerDistanceThresholdSpritePathSqr)
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
            
            _worldPath.Clear();

            int cornerCount = _path.corners.Length;
            
            for (int i = 1; i < cornerCount; i++)
            {
                Vector3 start = _path.corners[i - 1];
                Vector3 end = _path.corners[i];

                Vector3 segmentVec = end - start;
                float segmentLength = segmentVec.magnitude;

                while (distanceCovered + spacing <= segmentLength)
                {
                    distanceCovered += spacing;
                    float relativePosition = distanceCovered / segmentLength;
                    Vector3 position = Vector3.Lerp(start, end, relativePosition);

                    GameObject sprite = _navigationObjectPool.Get(position);
                    
                    if(sprite is not null)
                        _sprites.Add(sprite);

                    _worldPath.Add(position);
                }

                distanceCovered -= segmentLength;
            }
            
            if(changeLastSpriteColor)
                ChangeLastSpriteColor();
        }

        private void ChangeLastSpriteColor()
        {
            int index = _sprites.Count - 1;

            if (index >= 0 && _sprites[index] is not null)
            {
                SpriteRenderer lastSpriteRenderer = _navigationObjectPool.GetSpriteRenderer(_sprites[index]);
                if (lastSpriteRenderer is not null)
                {
                    Color color = lastSpriteColor;
                    color.a = 1f;
                    lastSpriteRenderer.color = color;
                }
            }
        }

        private void HideSprites()
        {
            for (int i = _sprites.Count - 1; i >= 0; i--)
            {
                if(_sprites[i] is not null && _sprites[i].activeSelf)
                    _navigationObjectPool.Release(_sprites[i]);
            }
            
            _sprites.Clear();
        }

        private void SetDestination(Transform targetTransform)
        {
            target = targetTransform;
            showToolTip = true;
            _pathUpdateTimer = pathSamplingRate; // Force immediate update
            UpdatePath();
            _pathNeedsRedraw = true;
            
            if (navigationToolTipType == NavigationToolTipType.SpriteLine)
                DrawSpritesFromZero();
        }

        private void SetNewMapCorner(Transform newCorner, NavigationMapCornerType cornerType)
        {
            switch (cornerType)
            {
                case NavigationMapCornerType.TopLeft:
                    topLeft = newCorner;
                    break;
                case NavigationMapCornerType.TopRight:
                    topRight = newCorner;
                    break;
                case NavigationMapCornerType.BottomLeft:
                    bottomLeft = newCorner;
                    break;
                case NavigationMapCornerType.BottomRight:
                    bottomRight = newCorner;
                    break;
            }
        }
        
        public void RefreshDistanceThresholds()
        {
            CacheSquaredDistances();
        }
    }
}