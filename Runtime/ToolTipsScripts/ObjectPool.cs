using System.Collections.Generic;
using UnityEngine;

namespace jeanf.tooltip
{
    public class ObjectPool : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private int poolSize = 10;

        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        void Start()
        {
            for (int i = 0; i < poolSize; i++)
            {
                GameObject obj = Instantiate(prefab, this.gameObject.transform);
                obj.SetActive(false);
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
            obj.SetActive(false);
            _pool.Enqueue(obj);
        }
    }
}
