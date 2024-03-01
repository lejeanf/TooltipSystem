using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using jeanf.EventSystem;

namespace jeanf.tooltip
{
    public class TooltipDisplayHandler : MonoBehaviour
    {
        [Header("ListeningOn")]
        [SerializeField] StringEventChannelSO stringEventChannelSO;

        [SerializeField] TextMeshProUGUI m_TextMeshProUGUI;


        private void OnEnable()
        {
            stringEventChannelSO.OnEventRaised += value => DisplayTooltip(value);
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            stringEventChannelSO.OnEventRaised += value => DisplayTooltip(value);
        }

        void DisplayTooltip(string tooltipToDisplay)
        {
            m_TextMeshProUGUI.text = tooltipToDisplay;
            m_TextMeshProUGUI.gameObject.SetActive(true);
        }

    }
}

