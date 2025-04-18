using UnityEngine;
using TMPro;
using jeanf.EventSystem;

namespace jeanf.tooltip
{
    public class TooltipDisplayHandler : MonoBehaviour
    {
        [Header("ListeningOn")]
        [SerializeField] StringBoolEventChannelSO stringBoolEventChannelSO;

        [SerializeField] TextMeshProUGUI TmpScreenUGUI;


        private void OnEnable()
        {
            stringBoolEventChannelSO.OnEventRaised += (str, hmdStatus) => DisplayTooltip(str, hmdStatus);
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            stringBoolEventChannelSO.OnEventRaised -= (str, hmdStatus) => DisplayTooltip(str, hmdStatus);
        }

        void DisplayTooltip(string tooltipToDisplay, bool hmdStatus)
        {
            if (hmdStatus)
            {
                Debug.Log(hmdStatus);
            }
            else if(!hmdStatus && TmpScreenUGUI != null)
            {
                TmpScreenUGUI.text = tooltipToDisplay;
                TmpScreenUGUI.gameObject.SetActive(true);
            }
        }
    }
}

