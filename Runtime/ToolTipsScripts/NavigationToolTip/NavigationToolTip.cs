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
        
        public delegate void UpdateNormalisedPathDelegate(float[][] normalisedPath);
        public static UpdateNormalisedPathDelegate UpdateNormalisedPath;
        
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
        
        //Cache to avoid allocations in DrawLine
        private Vector3[] _elevatedPathCache;
        
        //Cache for NavMeshHit (avoids boxing)
        private NavMeshHit _navMeshHit;
        
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
            _worldPath = new List<Vector3>(128); // Pré-allouer avec capacité
            _normalisedPath = null;
            _path = new NavMeshPath();
            _lineRenderer = GetComponent<LineRenderer>();
            _navigationObjectPool = GetComponent<NavigationObjectPool>();
            
            _playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
            _lastPlayerPosition = _playerTransform.position;
            
            _sprites = new List<GameObject>(128); // Pré-allouer avec capacité
            _elevatedPathCache = new Vector3[128]; // Pré-allouer le cache

            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = lineWidth;
            _lineRenderer.material = material;
            _lineRenderer.startColor = startColor;
            _lineRenderer.endColor = endColor;
            
            _lineRenderer.enabled = false;
            
            //Pre-calculate the distances squared
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

            return NavMesh.CalculatePath(_playerNavMeshPosition, 
                                            _targetNavMeshPosition, 
                                            NavMesh.AllAreas, _path) &&
                    _path.corners.Length > 0;
        }

        private void UpdatePath()
        {
            _playerNavMeshPosition = GetNearestNavMeshPoint(_playerTransform.position);
            _targetNavMeshPosition = GetNearestNavMeshPoint(target.position);

            NavMesh.CalculatePath(_playerNavMeshPosition,
                _targetNavMeshPosition,
                NavMesh.AllAreas, _path);
        }
        
        //Use ref to avoid copying struct
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
                _elevatedPathCache = new Vector3[cornerCount * 2]; // Double to avoid frequent reallocations
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
            _lineRenderer.positionCount = 0;
            _lineRenderer.enabled = false;
        }
        
        private void DrawSpritesPath()
        {
            if (_path.corners.Length == 0) return;
            
            if (IsPlayerGoingTowardPath())
            {
                CheckAndRemoveFirstSprite();
                _lastPlayerPosition = _playerTransform.position;
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
                
                NormalisePath();
                UpdateNormalisedPath?.Invoke(_normalisedPath);
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
            if (_sprites.Count == 0 || _playerTransform == null) return false;
                
            GameObject firstSprite = _sprites[0];
            Vector3 delta = _playerTransform.position - firstSprite.transform.position;
            return delta.sqrMagnitude < _playerDistanceThresholdFirstSpriteSqr;
        }
        
        private bool IsPlayerInMovement()
        {
            if (_playerTransform == null) return false;
            
            Vector3 delta = _playerTransform.position - _lastPlayerPosition;
            return delta.sqrMagnitude > _movementThresholdSqr;
        }

        private bool IsPlayerOnSpritePath()
        {
            if (_sprites.Count == 0 || _playerTransform == null)
                return false;

            Vector3 playerPos = _playerTransform.position;
            
            for (int i = 0; i < _sprites.Count; i++)
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
            
            NormalisePath();
            UpdateNormalisedPath?.Invoke(_normalisedPath);
        }

        private void ChangeLastSpriteColor()
        {
            int index = _sprites.Count - 1;

            if (index >= 0 && _sprites[index] != null)
            {
                SpriteRenderer lastSpriteRenderer = _navigationObjectPool.GetSpriteRenderer(_sprites[index]);
                if (lastSpriteRenderer != null)
                {
                    Color color = lastSpriteColor;
                    color.a = 1f;
                    lastSpriteRenderer.color = color;
                }
            }
        }

        private void HideSprites()
        {
            int count = transform.childCount;
    
            for (int i = count - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if(child.gameObject.activeSelf)
                    _navigationObjectPool.Release(child.gameObject);
            }
            
            _sprites.Clear();
        }
        
        private void NormalisePath()
        {
            int cornerCount = _path.corners.Length;
            
            if (cornerCount == 0) return;
            if (topLeft == null || topRight == null || bottomRight == null || bottomLeft == null) return;

            Vector3 topLeftPos = topLeft.position;
            Vector3 topRightPos = topRight.position;
            Vector3 bottomLeftPos = bottomLeft.position;
            
            float width = topLeftPos.x - topRightPos.x;
            float height = topLeftPos.z - bottomLeftPos.z;
            
            float invWidth = 1f / width;
            float invHeight = 1f / height;

            if (_normalisedPath == null || _normalisedPath.Length < cornerCount)
            {
                _normalisedPath = new float[cornerCount][];
                for (int i = 0; i < cornerCount; i++)
                {
                    _normalisedPath[i] = new float[2];
                }
            }
            
            for (int i = 0; i < cornerCount; i++)
            {
                Vector3 pos = _path.corners[i];
                
                float distanceFromTopLeftX = (topLeftPos.x - pos.x) * invWidth;
                float distanceFromTopLeftY = 1f - (1f - (topLeftPos.z - pos.z) * invHeight);
                
                _normalisedPath[i][0] = distanceFromTopLeftX;
                _normalisedPath[i][1] = distanceFromTopLeftY;
            }
        }

        private void SetDestination(Transform targetTransform)
        {
            target = targetTransform;
            showToolTip = true;
            UpdatePath();
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