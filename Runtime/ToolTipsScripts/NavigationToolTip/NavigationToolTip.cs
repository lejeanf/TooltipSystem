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
        
        private void Awake()
        {
            _worldPath = new List<Vector3>();
            _normalisedPath = null;
            _path = new NavMeshPath();
            _lineRenderer = GetComponent<LineRenderer>();
            _navigationObjectPool = GetComponent<NavigationObjectPool>();
            
            _playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
            _lastPlayerPosition = _playerTransform.position;
            
            _sprites = new List<GameObject>();

            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = lineWidth;
            _lineRenderer.material = material;
            _lineRenderer.startColor = startColor;
            _lineRenderer.endColor = endColor;
            
            _lineRenderer.enabled = false;
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
        
        Vector3 GetNearestNavMeshPoint(Vector3 position)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, navmeshDetectionDistance, NavMesh.AllAreas))
            {
                return hit.position;
            }
            return position;
        }

        private void DrawLine()
        {
            _lineRenderer.enabled = true;
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
            return Vector3.Distance(_playerTransform.position, target.position) <= destinationThreshold;
        }

        private void CheckAndRemoveFirstSprite()
        {
            if (_sprites.Count == 0) return;

            GameObject firstSprite = _sprites[0];

            if (Vector3.Distance(_playerTransform.position, firstSprite.transform.position) < spacing * 0.5f)
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
                Vector3 toNextSprite = (nextSprite.transform.position - _playerTransform.position).normalized;
                Vector3 playerMovement = (_playerTransform.position - _lastPlayerPosition).normalized;

                float movementSimilarity = Vector3.Dot(playerMovement, toNextSprite);

                return Vector3.Distance(_playerTransform.position, _lastPlayerPosition) > 0.01f && movementSimilarity > 0.5f;
            }
            return false;
        }

        private bool IsPlayerNearFirstSprite()
        {
            if (_sprites.Count == 0 || _playerTransform == null) return false;
                
            GameObject firstSprite = _sprites[0];
            float distance = Vector3.Distance(_playerTransform.position, firstSprite.transform.position);

            return distance < playerDistanceThresholdFirstSprite;
        }
        
        private bool IsPlayerInMovement()
        {
            float movementThreshold = 0.005f;
            
            if (_playerTransform == null) return false;
            
            float distanceMoved = Vector3.Distance(_playerTransform.position, _lastPlayerPosition);

            return distanceMoved > movementThreshold;
        }

        private bool IsPlayerOnSpritePath()
        {
            if (_sprites.Count == 0 || _playerTransform == null)
                return false;

            for (int i = 0; i < _sprites.Count; i++)
            {
                if (Vector3.Distance(_playerTransform.position, _sprites[i].transform.position) < playerDistanceThresholdSpritePath)
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
            SpriteRenderer lastSpriteRenderer;

            if (index >= 0 && _sprites[index] != null)
            {
                lastSpriteRenderer = _navigationObjectPool.GetSpriteRenderer(_sprites[index]);
                if (lastSpriteRenderer != null)
                {
                    lastSpriteColor.a = 1f;
                    lastSpriteRenderer.color = lastSpriteColor;
                }
            }
        }

        private void HideSprites()
        {
            int count = transform.childCount;
    
            for (int i = count - 1; i >= 0; i--)
            {
                GameObject sprite = transform.GetChild(i).gameObject;
                if(sprite.activeSelf)
                    _navigationObjectPool.Release(transform.GetChild(i).gameObject);
            }
            
            _sprites.Clear();
        }
        
        private void NormalisePath()
        {
            if (_path.corners == null || _path.corners.Length == 0) return;
            if (topLeft == null || topRight == null || bottomRight == null || bottomLeft == null) return;

            float width = topLeft.position.x - topRight.position.x;
            float height = topLeft.position.z - bottomLeft.position.z;

            float[][] result = new float[_path.corners.Length][];
            
            for (int i = 0; i < _path.corners.Length; i++)
            {
                result[i] = new float[2];
                Vector3 pos = _path.corners[i];
                
                float distanceFromTopLeftX = (topLeft.position.x - pos.x) / width;
                float distanceFromTopLeftY = 1 - (1 - (topLeft.position.z - pos.z) / height);
                
                result[i][0] = distanceFromTopLeftX;
                result[i][1] = distanceFromTopLeftY;
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
    }
}