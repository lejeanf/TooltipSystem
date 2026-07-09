using System;
using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableTooltipService
    {
        private Vector3 _originalTooltipSize = Vector3.one;
        private Vector3 _tooltipSizeWhenHidden = Vector3.one;
        private CanvasGroup _canvasGroup;
        private readonly float _animationDuration = 0f;
        private readonly TooltipAnimationEnum _animationType;
        private MotionHandle _motionHandle;
        private InteractableTooltip _interactableTooltip;

        private readonly GameObject _tooltip;
        
        
        public InteractableTooltipService(InteractableTooltip interactableTooltip, InteractableTooltipSettingsSo interactableTooltipSettingsSo)
        {
            _tooltip = interactableTooltip.TooltipClose;
            _canvasGroup = _tooltip.GetComponent<CanvasGroup>();
                        
            _animationDuration = interactableTooltipSettingsSo.animationSo.animationDuration;
            _animationType = interactableTooltipSettingsSo.animationSo.animationType;
            
            _interactableTooltip = interactableTooltip;
            
            AnimationSetUp(interactableTooltipSettingsSo);
            
            _tooltip.SetActive(false);
        }
        
        private GameObject CreateTextObject(string name, InteractableTooltipSettingsSo settings)
        {
            GameObject textObject = new GameObject(name);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            return textObject;
        }
        
        private void AddChildToParent(GameObject child, GameObject parent)
        {
            child.transform.SetParent(parent.transform, false);
            ContentSizeFitter fitter = child.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void AnimationSetUp(InteractableTooltipSettingsSo settings)
        {
            switch (_animationType)
            {
                case TooltipAnimationEnum.Pop:
                    _originalTooltipSize = _tooltip.transform.localScale;
                    _tooltipSizeWhenHidden = _tooltip.transform.localScale * settings.animationSo.iconSizeModifierWhenHidden;
                    _tooltip.transform.localScale = _tooltipSizeWhenHidden;
                    _canvasGroup.alpha = 1f;
                    break;
                case TooltipAnimationEnum.Fade:
                    _canvasGroup.alpha = 0f;
                    break;
            }
        }

        public void ShowIcons()
        {
            _tooltip.SetActive(true);
            
            switch (_animationType)
            {
                case TooltipAnimationEnum.Pop:
                    PlayPopInAnimation();
                    break;
                case TooltipAnimationEnum.Fade:
                    PlayFadeInAnimation();
                    break;
            }
        }

        public void HideIcons()
        {
            switch (_animationType)
            {
                case TooltipAnimationEnum.Pop:
                    PlayPopOutAnimation();
                    break;
                case TooltipAnimationEnum.Fade:
                    PlayFadeOutAnimation();
                    break;
            }
            
            try 
            {
                if(_motionHandle.IsActive())
                    _motionHandle.GetAwaiter().OnCompleted(() => _interactableTooltip.ShowCloseTooltip());
            }
            catch (NullReferenceException)
            {
                return;
            }
        }

        private void PlayPopInAnimation()
        {
            if (_tooltip == null) return;
            if (_tooltip.transform.localScale != _originalTooltipSize)
                _motionHandle = LMotion
                    .Create(_tooltip.transform.localScale, _originalTooltipSize, _animationDuration)
                    .WithEase(Ease.InOutSine)
                    .Bind(x =>
                    {
                        if (_tooltip == null) return;
                            _tooltip.transform.localScale = x;
                    })
                    ;
        }

        private void PlayPopOutAnimation()
        {
            if (_tooltip == null) return;
            if (_tooltip.transform.localScale != _tooltipSizeWhenHidden)
                _motionHandle = LMotion
                    .Create(_tooltip.transform.localScale, _tooltipSizeWhenHidden, _animationDuration)
                    .WithEase(Ease.InOutSine)
                    .Bind(x =>
                    {
                        if (_tooltip == null) return;
                        _tooltip.transform.localScale = x;
                    });
        }

        private void PlayFadeInAnimation()
        {
            if (_canvasGroup == null) return;
            _motionHandle = LMotion
                .Create(_canvasGroup.alpha, 1f, _animationDuration * 1.75f)
                .WithEase(Ease.Linear)
                .Bind(x =>
                {
                    if (_canvasGroup == null) return;
                    _canvasGroup.alpha = x;
                });
        }

        private void PlayFadeOutAnimation()
        {
            if (_canvasGroup == null) return;
            _motionHandle = LMotion
                .Create(_canvasGroup.alpha, 0f, _animationDuration)
                .WithEase(Ease.Linear)
                .Bind(x =>
                {
                    if (_canvasGroup == null) return;
                    _canvasGroup.alpha = x;
                });
        }
        
        public void Destroy()
        {
            try 
            {
                if (!_motionHandle.IsActive()) return; 
                _motionHandle.Cancel(); 
            }
            catch (NullReferenceException)
            {
                return;
            }
        }
    }
}
