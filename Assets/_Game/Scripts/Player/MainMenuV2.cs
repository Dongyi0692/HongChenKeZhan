using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace FengChenInn {
public sealed class MainMenuV2 : MonoBehaviour {
    RectTransform board;
    CanvasGroup canvas;
    readonly List<RectTransform> rain = new();
    readonly List<Image> lights = new();
    readonly List<CanvasGroup> artwork = new();
    readonly List<float> delays = new();
    float appeared;
    bool seen, leaving;
    Texture2D glowTexture;
    Sprite glowSprite;
    public void Build(Action start, Action settings, Action quit) {
        var bg = new GameObject("Menu Letterbox",typeof(RectTransform),typeof(Image));
        bg.transform.SetParent(transform,false); var br=(RectTransform)bg.transform;
        br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;br.offsetMin=br.offsetMax=Vector2.zero;
        bg.GetComponent<Image>().color=Color.black;
        board=new GameObject("红尘客栈 · 第二版",typeof(RectTransform),typeof(CanvasGroup)).GetComponent<RectTransform>();
        board.SetParent(transform,false);board.sizeDelta=new Vector2(1672,941);
        canvas=board.GetComponent<CanvasGroup>();
        Action[] actions={start,settings,quit};
        AddArt(0, 0, 0, 1672, 941);
        AddArt(1, 0, 0, 1672, 941);
        AddArt(2, 150, 83, 272, 33);
        AddArt(3, 83, 151, 420, 148);
        AddArt(4, 111, 339, 364, 42);
        AddArt(5, 129, 846, 307, 35);
        AddArt(6, 502, 180, 49, 89);
        AddArt(7, 0, 0, 1672, 941);
        AddArt(8, 0, 0, 1672, 941);
        AddArt(9, 0, 0, 1672, 941);
        AddButton(10, 11, 93, 438, 387, 90, actions[0]);
        AddButton(12, 13, 112, 550, 354, 77, actions[1]);
        AddButton(14, 15, 112, 647, 354, 77, actions[2]);
        MakeWeather();
        appeared=Time.unscaledTime;
    }
    void OnEnable(){appeared=Time.unscaledTime; leaving=false;if(canvas){canvas.interactable=true;canvas.blocksRaycasts=true;}}
    RawImage Art(int id,float x,float y,float w,float h){
        var im=new GameObject("Art "+id,typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
        im.transform.SetParent(board,false);var r=im.rectTransform;
        r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(.5f,.5f);
        r.anchoredPosition=new Vector2(x+w*.5f,-y-h*.5f);r.sizeDelta=new Vector2(w,h);
        im.texture=Resources.Load<Texture2D>("MainMenuV2/asset_"+id.ToString("00")); im.raycastTarget=false;
        if(!im.texture)Debug.LogError("Missing menu image "+id);
        return im;
    }
    void AddArt(int id,float x,float y,float w,float h){
        var im=Art(id,x,y,w,h);
        if(id>1){var g=im.gameObject.AddComponent<CanvasGroup>();artwork.Add(g);delays.Add(id==3?.45f:.7f);}
    }
    void AddButton(int normal,int hover,float x,float y,float w,float h,Action action){
        var im=Art(normal,x,y,w,h);im.raycastTarget=true;
        var button=im.gameObject.AddComponent<Button>();button.targetGraphic=im;button.transition=Selectable.Transition.None;
        var motion=im.gameObject.AddComponent<MenuButtonV2>();motion.normal=im.texture;motion.hover=Resources.Load<Texture2D>("MainMenuV2/asset_"+hover.ToString("00"));
        button.onClick.AddListener(()=>{if(!leaving)StartCoroutine(Leave(action));});
        var group=im.gameObject.AddComponent<CanvasGroup>();artwork.Add(group);delays.Add(1.0f+(normal-10)*.08f);
    }
    IEnumerator Leave(Action action){
        leaving=true;canvas.interactable=false;
        for(float t=0;t<.22f;t+=Time.unscaledDeltaTime){canvas.alpha=1-t/.22f;yield return null;}
        action();
    }
    void MakeWeather(){
        var weather=new GameObject("Rain and lantern light",typeof(RectTransform)).GetComponent<RectTransform>();
        weather.SetParent(board,false);weather.anchorMin=Vector2.zero;weather.anchorMax=Vector2.one;weather.offsetMin=weather.offsetMax=Vector2.zero;
        weather.SetSiblingIndex(1);
        var random=new System.Random(12);
        for(int i=0;i<75;i++){
            var im=new GameObject("Rain",typeof(RectTransform),typeof(Image)).GetComponent<Image>();im.transform.SetParent(weather,false);im.raycastTarget=false;
            var r=im.rectTransform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.anchoredPosition=new Vector2(560+(float)random.NextDouble()*1120,-(float)random.NextDouble()*941);
            r.sizeDelta=new Vector2(i%3==0?1.1f:.6f,14+(float)random.NextDouble()*22);r.localEulerAngles=new Vector3(0,0,-17);
            im.color=new Color(.66f,.77f,.86f,i%3==0?.15f:.07f);rain.Add(r);
        }
        glowTexture=new Texture2D(64,64,TextureFormat.RGBA32,false);
        for(int y=0;y<64;y++)for(int x=0;x<64;x++){float a=Mathf.Pow(Mathf.Clamp01(1-Vector2.Distance(new Vector2(x,y),new Vector2(31.5f,31.5f))/32),2);glowTexture.SetPixel(x,y,new Color(1,.55f,.16f,a));}
        glowTexture.Apply();glowSprite=Sprite.Create(glowTexture,new Rect(0,0,64,64),new Vector2(.5f,.5f));
        foreach(var p in new[]{new Vector2(1354,-340),new Vector2(1183,-512),new Vector2(1608,-480),new Vector2(1335,-824)}){
            var im=new GameObject("Lantern breathing",typeof(RectTransform),typeof(Image)).GetComponent<Image>();im.transform.SetParent(weather,false);im.raycastTarget=false;im.sprite=glowSprite;
            im.rectTransform.anchorMin=im.rectTransform.anchorMax=new Vector2(0,1);im.rectTransform.anchoredPosition=p;im.rectTransform.sizeDelta=new Vector2(160,190);lights.Add(im);
        }
    }
    void Update(){
        if(!board)return;
        var size=((RectTransform)transform).rect.size;board.localScale=Vector3.one*Mathf.Min(size.x/1672,size.y/941);
        float elapsed=Time.unscaledTime-appeared;
        if(!leaving)canvas.alpha=Mathf.Clamp01(elapsed/(seen?.25f:1.2f));
        for(int i=0;i<artwork.Count;i++){artwork[i].alpha=Mathf.Clamp01((elapsed-(seen?0:delays[i]))/.65f);artwork[i].interactable=artwork[i].alpha>.95f;}
        if(elapsed>3)seen=true;
        for(int i=0;i<rain.Count;i++){
            var p=rain[i].anchoredPosition;p+=new Vector2(-52,-180-(i*37)%260)*Time.unscaledDeltaTime;
            p.y=Mathf.Repeat(p.y+970,1000)-970;
            p.x=560+Mathf.Repeat(p.x-560,1120);rain[i].anchoredPosition=p;
        }
        for(int i=0;i<lights.Count;i++)lights[i].color=new Color(1,1,1,.14f+.035f*Mathf.Sin(Time.unscaledTime*1.3f+i));
    }
    void OnDestroy(){if(glowSprite)Destroy(glowSprite);if(glowTexture)Destroy(glowTexture);}
}
public sealed class MenuButtonV2 : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler {
    public Texture normal,hover; bool over,down;
    public void OnPointerEnter(PointerEventData e){over=true;}
    public void OnPointerExit(PointerEventData e){over=false;down=false;}
    public void OnPointerDown(PointerEventData e){down=true;}
    public void OnPointerUp(PointerEventData e){down=false;}
    public void OnSelect(BaseEventData e){over=true;}
    public void OnDeselect(BaseEventData e){over=false;down=false;}
    void OnDisable(){over=down=false;transform.localScale=Vector3.one;}
    void Update(){GetComponent<RawImage>().texture=over?hover:normal;float scale=down?.98f:over?1.02f:1;transform.localScale=Vector3.Lerp(transform.localScale,Vector3.one*scale,1-Mathf.Exp(-18*Time.unscaledDeltaTime));}
}}
