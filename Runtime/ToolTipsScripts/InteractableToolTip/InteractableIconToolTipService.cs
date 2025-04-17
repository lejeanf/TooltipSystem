using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableIconToolTipService
    {
        private bool _isMultipleIcons = false;

        private Vector3 _originalToolTipSize = Vector3.one;
        private Vector3 _tooltipSizeWhenHidden = Vector3.one;
        private CanvasGroup _canvasGroup;
        private float _animationDuration = 0f;
        private ToolTipAnimationEnum _animationType;
        private MotionHandle _motionHandle;
        
        private GameObject _iconGameObjectParent;
        private GameObject _iconGameObjectLeft;
        private GameObject _iconGameObjectRight;
        private GameObject _textInBetweenIconGameObject;
        
        private Image _iconImageLeft;
        private Image _iconImageRight;
        
        private Canvas _iconCanvas;
        private HorizontalLayoutGroup _iconHorizontalLayoutGroup;
        
        #region Forge

        public GameObject ForgeIconGameObject(Sprite sprite1, InteractableToolTipSettingsSo interactableToolTipSettingsSo)
        {
            _iconGameObjectParent = ForgeIconGameObject(sprite1, null, interactableToolTipSettingsSo);

            _isMultipleIcons = false;
            
            return _iconGameObjectParent;
        }
        
        public GameObject ForgeIconGameObject(Sprite sprite1, Sprite sprite2, InteractableToolTipSettingsSo interactableToolTipSettingsSo)
        {
            _iconGameObjectParent = new GameObject("ToolTip Icon");

            _iconCanvas = _iconGameObjectParent.AddComponent<Canvas>();
            
            _canvasGroup = _iconGameObjectParent.AddComponent<CanvasGroup>();

            _iconCanvas.renderMode = RenderMode.WorldSpace;

            _iconCanvas.transform.localScale = Vector3.one * 0.5f;
            
            RectTransform rectTransform = _iconGameObjectParent.GetComponent<RectTransform>();
            if (rectTransform is null)
            {
                rectTransform = _iconGameObjectParent.AddComponent<RectTransform>();
            }
            
            rectTransform.sizeDelta = new Vector2(interactableToolTipSettingsSo.canvasSizeX, interactableToolTipSettingsSo.canvasSizeY);
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.localPosition = Vector3.zero;
            
            _iconHorizontalLayoutGroup = _iconGameObjectParent.AddComponent<HorizontalLayoutGroup>();
            _iconHorizontalLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
            _iconHorizontalLayoutGroup.spacing = interactableToolTipSettingsSo.iconSpacing;
            int padding = interactableToolTipSettingsSo.iconPadding;
            _iconHorizontalLayoutGroup.padding = new RectOffset(padding, padding, padding, padding);
            _iconHorizontalLayoutGroup.childForceExpandHeight = false;
            _iconHorizontalLayoutGroup.childForceExpandWidth = false;
            
            _iconHorizontalLayoutGroup.childControlWidth = true;
            _iconHorizontalLayoutGroup.childControlHeight = true;

            _iconGameObjectLeft = CreateIconObject("ToolTip Icon 1", sprite1, interactableToolTipSettingsSo);
            _iconImageLeft = _iconGameObjectLeft.GetComponent<Image>();
            _textInBetweenIconGameObject = CreateTextObject("Text InBetween", interactableToolTipSettingsSo);
            _iconGameObjectRight = CreateIconObject("ToolTip Icon 2", sprite2, interactableToolTipSettingsSo);
            _iconImageRight = _iconGameObjectRight.GetComponent<Image>();
            
            AddChildToParent(_iconGameObjectLeft, _iconGameObjectParent);
            AddChildToParent(_textInBetweenIconGameObject, _iconGameObjectParent);
            AddChildToParent(_iconGameObjectRight, _iconGameObjectParent);

            _animationDuration = interactableToolTipSettingsSo.animationDuration;
            
            _isMultipleIcons = true;
            
            _animationType = interactableToolTipSettingsSo.animationType;
            
            IconAnimationSetUp(interactableToolTipSettingsSo);
            
            DeactivateToolTip();

            return _iconGameObjectParent;
        }
        
        private GameObject CreateIconObject(string name, Sprite sprite, InteractableToolTipSettingsSo settings)
        {
            GameObject iconObject = new GameObject(name);
            
            RectTransform rectTransform = iconObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            
            Image image = iconObject.AddComponent<Image>();
            image.material = new Material(settings.iconMaterial);
            image.sprite = sprite;
            image.preserveAspect = true;
            
            LayoutElement layoutElement = iconObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = settings.iconSize; 
            layoutElement.preferredHeight = settings.iconSize;
            
            iconObject.transform.localScale = Vector3.one * settings.iconSize; 

            return iconObject;
        }
        
        private GameObject CreateTextObject(string name, InteractableToolTipSettingsSo settings)
        {
            GameObject textObject = new GameObject(name);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = settings.textInBetweenIcons;
            text.alignment = TextAlignmentOptions.Center;
            text.font = settings.textFont;
            text.fontSize = settings.fontSizeForIconMode;
            
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

        private void IconAnimationSetUp(InteractableToolTipSettingsSo settings)
        {
            switch (_animationType)
            {
                case ToolTipAnimationEnum.Pop:
                    _originalToolTipSize = _iconGameObjectParent.transform.localScale;
                    _tooltipSizeWhenHidden = _iconGameObjectParent.transform.localScale * settings.iconSizeModifierWhenHidden;
                    _iconGameObjectParent.transform.localScale = _tooltipSizeWhenHidden;
                    break;
                case ToolTipAnimationEnum.Fade:
                    _canvasGroup.alpha = 0f;
                    break;
            }
            
        }

        #endregion
        
        public void ChangeSprite(Sprite sprite)
        {
            _iconImageLeft.sprite = sprite;
            _isMultipleIcons = false;
        }

        public void ChangeSprite(Sprite sprite1, Sprite sprite2)
        {
            _iconImageLeft.sprite = sprite1;
            _iconImageRight.sprite = sprite2;
            _isMultipleIcons = true;
        }

        public void ShowIcons()
        {
            _iconGameObjectParent.SetActive(true);
            _iconGameObjectLeft.SetActive(true);
            
            if (_isMultipleIcons)
            {
                _iconGameObjectRight.SetActive(true);
                _textInBetweenIconGameObject.SetActive(true);
            }
            else
            {
                _iconGameObjectRight.SetActive(false);
                _textInBetweenIconGameObject.SetActive(false);

                _iconGameObjectLeft.transform.localPosition = Vector3.zero;
            }
            
            //Pop works better without the if
            if(!_motionHandle.IsActive())
                switch (_animationType)
                {
                    case ToolTipAnimationEnum.Pop:
                        PlayPopInAnimation();
                        break;
                    case ToolTipAnimationEnum.Fade:
                        PlayFadeInAnimation();
                        break;
                }
        }

        public void HideIcons()
        {
            switch (_animationType)
            {
                case ToolTipAnimationEnum.Pop:
                    PlayPopOutAnimation();
                    break;
                case ToolTipAnimationEnum.Fade:
                    PlayFadeOutAnimation();
                    break;
            }
            
            if(_motionHandle.IsActive())
                _motionHandle.GetAwaiter().OnCompleted(DeactivateToolTip);
        }

        private void PlayPopInAnimation()
        {
            if (_iconGameObjectParent.transform.localScale != _originalToolTipSize)
                _motionHandle = LMotion
                    .Create(_iconGameObjectParent.transform.localScale, _originalToolTipSize, _animationDuration)
                    .WithEase(Ease.InOutSine)
                    .Bind(x => _iconGameObjectParent.transform.localScale = x);
        }

        private void PlayPopOutAnimation()
        {
            if (_iconGameObjectParent.transform.localScale != _tooltipSizeWhenHidden)
                _motionHandle = LMotion
                    .Create(_iconGameObjectParent.transform.localScale, _tooltipSizeWhenHidden, _animationDuration)
                    .WithEase(Ease.InOutSine)
                    .Bind(x => _iconGameObjectParent.transform.localScale = x);
        }

        private void PlayFadeInAnimation()
        {
            _motionHandle = LMotion
                .Create(_canvasGroup.alpha, 1f, _animationDuration * 1.75f)
                .WithEase(Ease.Linear)
                .Bind(x => _canvasGroup.alpha = x);
        }

        private void PlayFadeOutAnimation()
        {
            _motionHandle = LMotion
                .Create(_canvasGroup.alpha, 0f, _animationDuration)
                .WithEase(Ease.Linear)
                .Bind(x => _canvasGroup.alpha = x);
        }

        private void DeactivateToolTip()
        {
            _iconGameObjectParent.SetActive(false);
            _iconGameObjectLeft.SetActive(false);
            _iconGameObjectRight.SetActive(false);
            _textInBetweenIconGameObject.SetActive(false);
        }
        
        public void Destroy()
        {
            if(!_motionHandle.IsActive()) return;
            _motionHandle.Complete();
            _motionHandle.Cancel();
        }
    }
}
