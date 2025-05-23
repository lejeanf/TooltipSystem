using System.Collections.Generic;
using System.Linq;
using jeanf.propertyDrawer;
using UnityEngine;

namespace jeanf.tooltip
{
    [ScriptableObjectDrawer]
    [CreateAssetMenu(fileName = "InputIconSo", menuName = "Tooltips/InputIconSo", order = 1)]
    public class InputIconSo : ScriptableObject
    {
        public Sprite xrGrip;
        public Sprite xrPoke;
        public Sprite gamepadRT;
        public Sprite keyboardLMB;
        public Sprite keyboardE;
        
        private Dictionary<string, Sprite> inputsDictionary;

        private void OnEnable()
        {
            inputsDictionary = new Dictionary<string, Sprite>();
            inputsDictionary.Add("gripPressed", xrGrip);
            inputsDictionary.Add("pokePosition", xrPoke);
            inputsDictionary.Add("RT", gamepadRT);
            inputsDictionary.Add("LMB", keyboardLMB);
            inputsDictionary.Add("E", keyboardE);
        }

        public Sprite GetInputIcon(string inputName)
        {
            return inputsDictionary
                .Where(pair => inputName.Contains(pair.Key))
                .Select(pair => pair.Value)
                .First();
        }
    }
}