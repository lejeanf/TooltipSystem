using System.Collections.Generic;
using UnityEngine;

namespace jeanf.tooltip
{
    public class NavigationObjectPool : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private int poolSize = 10;

        private readonly Queue<GameObject> _pool = new Queue<GameObject>();
        private readonly Dictionary<GameObject, SpriteRenderer> _imageDictionary = new Dictionary<GameObject, SpriteRenderer>();
        
        private Color _baseObjectColor;

        void Start()
        {
            for (int i = 0; i < poolSize; i++)
            {
                GameObject obj = Instantiate(prefab, gameObject.transform);
                obj.SetActive(false);
                SpriteRenderer spriteRenderer = obj.GetComponent<SpriteRenderer>();
                _baseObjectColor = spriteRenderer.color;
                
                if (spriteRenderer != null)
                    _imageDictionary.Add(obj, spriteRenderer);
                _pool.Enqueue(obj);
            }
        }

        public GameObject Get(Vector3 position)
        {
            GameObject obj;
            if (_pool.Count > 0)
            {
                obj = _pool.Dequeue();
            }
            else
            {
                return null;
            }
            
            if(obj is null) return null;
            
            obj.transform.position = position;
            obj.SetActive(true);
            return obj;
        }

        public void Release(GameObject obj)
        {
            SpriteRenderer spriteRenderer = _imageDictionary[obj];
            
            if (spriteRenderer != null)
                spriteRenderer.color = _baseObjectColor;
            
            obj.SetActive(false);
            _pool.Enqueue(obj);
        }

        public SpriteRenderer GetSpriteRenderer(GameObject obj)
        {
            if (_pool.Count > 0)
                return _imageDictionary[obj];
            else
                return null;
        }
    }
}
