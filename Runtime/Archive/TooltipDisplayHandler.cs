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
            stringBoolEventChannelSO.OnEventRaised += DisplayTooltip;
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            stringBoolEventChannelSO.OnEventRaised -= DisplayTooltip;
        }

        private void DisplayTooltip(string tooltipToDisplay, bool hmdStatus)
        {
            if (hmdStatus)
            {
                Debug.Log(hmdStatus);
            }
            else if(!hmdStatus && TmpScreenUGUI)
            {
                TmpScreenUGUI.text = tooltipToDisplay;
                TmpScreenUGUI.gameObject.SetActive(true);
            }
        }
    }
}

