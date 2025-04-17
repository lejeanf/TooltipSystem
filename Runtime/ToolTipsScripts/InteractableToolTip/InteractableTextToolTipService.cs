using LitMotion;
using TMPro;
using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableTextToolTipService
    {
        private float _animationDuration = 0;
        private MotionHandle _motionHandle;
        
        private float _targetFontSize;
        private float _fontSizeWhenHidden;

        private ToolTipAnimationEnum _animationType;
        
        private TMP_Text _text;
        private GameObject _textGameObject;
        
        public GameObject ForgeTextGameObject(string startingText, InteractableToolTipSettingsSo interactableToolTipSettingsSo)
        {
            _textGameObject = new GameObject("ToolTip Text");
            _textGameObject.transform.localPosition = Vector3.zero;
            
            _text = _textGameObject.AddComponent<TextMeshPro>();
            _text.fontSize = interactableToolTipSettingsSo.fontSizeForTextMode;
            _text.alignment = TextAlignmentOptions.Center;
            _text.font = interactableToolTipSettingsSo.textFont;
            _text.text = startingText;
            
            _animationType = interactableToolTipSettingsSo.animationType;
            TextAnimationSetup(interactableToolTipSettingsSo);
            
            return _textGameObject;
        }
        
        private void TextAnimationSetup(InteractableToolTipSettingsSo interactableToolTipSettingsSo)
        {
            switch (_animationType)
            {
                case ToolTipAnimationEnum.Pop:
                    _targetFontSize = _text.fontSize;
                    _fontSizeWhenHidden = _text.fontSize * interactableToolTipSettingsSo.fontSizeModifierWhenHidden;
                    _text.fontSize = _fontSizeWhenHidden;
                    break;
                case ToolTipAnimationEnum.Fade:
                    Color color = _text.color;
                    color.a = 0f;
                    _text.color = color;
                    break;
            }
            
            _animationDuration = interactableToolTipSettingsSo.animationDuration;
        }
        
        public void ChangeText(string newText)
        {
            _text.text = newText;
        }

        public void ShowText()
        {
            _textGameObject.SetActive(true);
            
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

        public void HideText()
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
            _motionHandle = LMotion
                .Create(_text.fontSize, _targetFontSize, _animationDuration)
                .WithEase(Ease.InOutSine)
                .Bind(x => _text.fontSize = x);
        }

        private void PlayPopOutAnimation()
        {
            _motionHandle = LMotion
                .Create(_text.fontSize, _fontSizeWhenHidden, _animationDuration)
                .WithEase(Ease.InOutSine)
                .Bind(x => _text.fontSize = x);
        }
        
        private void PlayFadeInAnimation()
        {
            _motionHandle = LMotion
                .Create(_text.color.a, 1f, _animationDuration * 1.75f)
                .WithEase(Ease.Linear)
                .Bind(x => 
                {
                    Color color = _text.color;
                    color.a = x;
                    _text.color = color;
                });
        }

        private void PlayFadeOutAnimation()
        {
            _motionHandle = LMotion
                .Create(_text.color.a, 0f, _animationDuration * 1.75f)
                .WithEase(Ease.Linear)
                .Bind(x => 
                {
                    Color color = _text.color;
                    color.a = x;
                    _text.color = color;
                });
        }

        private void DeactivateToolTip()
        {
            _textGameObject.SetActive(false);
        }
        
        public void Destroy()
        {
            if(!_motionHandle.IsActive()) return;
            _motionHandle.Complete();
            _motionHandle.Cancel();
        }
    }
}