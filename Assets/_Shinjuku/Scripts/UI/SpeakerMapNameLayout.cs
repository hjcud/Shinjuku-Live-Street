using TMPro;
using UdonSharp;
using UnityEngine;

// Local, event-driven layout for the six map labels. Never moves source icons.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SpeakerMapNameLayout : UdonSharpBehaviour
{
    public RectTransform mapRect;
    public RectTransform[] protectedAreas;
    public RectTransform overflowPanel;
    public TextMeshProUGUI[] overflowRows;
    public float nameFontSize=32f;
    [System.NonSerialized] private RectTransform[] icons;
    [System.NonSerialized] private TextMeshProUGUI[] names;
    [System.NonSerialized] private Vector4[] boxes;
    [System.NonSerialized] private int[] choices;
    [System.NonSerialized] private float[] widths;
    [System.NonSerialized] private Vector4[] obstacles;
    [System.NonSerialized] private Vector3[] corners=new Vector3[4];
    [System.NonSerialized] private Vector4 panelBox;

    public void Layout(RectTransform[] markers,TextMeshProUGUI[] labels)
    {
        if(mapRect==null||markers==null||labels==null)return;
        icons=markers;names=labels;int n=markers.Length;
        if(choices==null||choices.Length!=n)
        {
            choices=new int[n];boxes=new Vector4[n];widths=new float[n];
            for(int i=0;i<n;i++)choices[i]=-1;
        }
        int count=protectedAreas==null?0:protectedAreas.Length;
        if(obstacles==null||obstacles.Length!=count)obstacles=new Vector4[count];
        for(int i=0;i<count;i++)obstacles[i]=Bounds(protectedAreas[i]);
        if(overflowPanel!=null)overflowPanel.gameObject.SetActive(false);
        if(overflowRows!=null)for(int i=0;i<overflowRows.Length;i++)overflowRows[i].gameObject.SetActive(false);
        for(int i=0;i<n;i++)
        {
            boxes[i]=Vector4.zero;
            if(!icons[i].gameObject.activeSelf){choices[i]=-1;continue;}
            widths[i]=NameWidth(names[i]);
            HideNames(i);
        }
        // Stable source order and previous candidate preference prevent side flipping.
        for(int i=0;i<n;i++)
        {
            if(!icons[i].gameObject.activeSelf)continue;
            int selected=-1;
            if(choices[i]>=0&&choices[i]<6&&Fits(Candidate(i,choices[i]),i))selected=choices[i];
            if(selected<0)for(int c=0;c<6;c++)if(Fits(Candidate(i,c),i)){selected=c;break;}
            choices[i]=selected;
            if(selected>=0){boxes[i]=Candidate(i,selected);ShowName(i,selected,boxes[i]);}
        }
        int overflow=0;Vector2 center=Vector2.zero;
        for(int i=0;i<n;i++)if(icons[i].gameObject.activeSelf&&choices[i]<0){overflow++;center+=Point(i);}
        if(overflow==0||overflowPanel==null)return;
        center/=overflow;
        float w=320,h=overflow*50+16;
        Vector4 best=Vector4.zero;float score=float.MaxValue;
        // Keep a previously valid list location; otherwise find the nearest free map area.
        Vector4 old=new Vector4(panelBox.x,panelBox.y,w,h);
        if(Fits(old,-1)&&panelBox.z>0)best=old;
        else
        {
            for(float y=8;y+h<=mapRect.rect.height-8;y+=28)
            for(float x=8;x+w<=mapRect.rect.width-8;x+=32)
            {
                Vector4 b=new Vector4(x,y,w,h);
                if(!Fits(b,-1))continue;
                float d=(new Vector2(x+w*.5f,y+h*.5f)-center).sqrMagnitude;
                if(d<score){score=d;best=b;}
            }
        }
        if(best.z<=0)return; // Defensive: authored maps always provide space for six rows.
        panelBox=best;Position(overflowPanel,best);overflowPanel.gameObject.SetActive(true);
        int row=0;
        for(int i=0;i<n;i++)
        {
            if(!icons[i].gameObject.activeSelf||choices[i]>=0)continue;
            var text=overflowRows[row];text.text=(i+1)+"  "+names[i].text;text.gameObject.SetActive(true);
            text.GetComponent<RectTransform>().anchoredPosition=new Vector2(10,-8-row*50);row++;
            // Number badges share one compact comma-separated badge when icons overlap.
            int group=i;
            for(int j=0;j<i;j++)if(icons[j].gameObject.activeSelf&&choices[j]<0&&(Point(i)-Point(j)).sqrMagnitude<48*48){group=j;break;}
            var badge=icons[group].Find("NameNumber").GetComponent<TextMeshProUGUI>();
            badge.text=badge.gameObject.activeSelf?badge.text+","+(i+1):(i+1).ToString();
            badge.gameObject.SetActive(true);
            float bw=Mathf.Min(200,44+badge.text.Length*16);
            Vector2 p=Point(group);
            Vector4 bb=new Vector4(Mathf.Clamp(p.x-bw*.5f,8,mapRect.rect.width-bw-8),Mathf.Clamp(p.y+30,8,mapRect.rect.height-58),bw,50);
            float badgeScore=float.MaxValue;
            for(float by=8;by+50<=mapRect.rect.height-8;by+=28)for(float bx=8;bx+bw<=mapRect.rect.width-8;bx+=32)
            {
                Vector4 option=new Vector4(bx,by,bw,50);
                if(!Fits(option,group))continue;
                float d=(new Vector2(option.x+bw*.5f,option.y+25)-p).sqrMagnitude;
                if(d<badgeScore){bb=option;badgeScore=d;}
            }
            boxes[group]=bb;
            badge.GetComponent<RectTransform>().sizeDelta=new Vector2(bw,50);
            Vector2 target=new Vector2(bb.x+bw*.5f-p.x,bb.y+25-p.y);
            badge.GetComponent<RectTransform>().anchoredPosition=target;
            var tether=(RectTransform)icons[group].Find("NameLeader");
            Vector2 start=target.normalized*22,end=target-target.normalized*25,direction=end-start;
            tether.anchoredPosition=(start+end)*.5f;tether.sizeDelta=new Vector2(direction.magnitude,1.5f);tether.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);tether.gameObject.SetActive(true);
        }
    }
    private float NameWidth(TextMeshProUGUI name)
    {
        float width=16;
        for(int i=0;i<name.text.Length;i++)width+=nameFontSize*(name.text[i]>=0x2e80?1.05f:.85f);
        return Mathf.Clamp(width,75,260);
    }
    private Vector2 Point(int i)
    {
        Vector3 p=mapRect.InverseTransformPoint(icons[i].position);
        return new Vector2(p.x-mapRect.rect.xMin,p.y-mapRect.rect.yMin);
    }
    private Vector4 Candidate(int i,int c)
    {
        Vector2 p=Point(i);float y=c<2?0:(c<4?56:-56);bool right=c%2==0;
        return new Vector4(right?p.x+31:p.x-31-widths[i],p.y+y-25,widths[i],50);
    }
    private bool Overlap(Vector4 a,Vector4 b)
    {
        return a.z>0&&b.z>0&&a.x<b.x+b.z+4&&a.x+a.z+4>b.x&&a.y<b.y+b.w+4&&a.y+a.w+4>b.y;
    }
    private bool Fits(Vector4 b,int self)
    {
        if(b.z<=0||b.x<8||b.y<8||b.x+b.z>mapRect.rect.width-8||b.y+b.w>mapRect.rect.height-8)return false;
        if(overflowPanel!=null&&overflowPanel.gameObject.activeSelf&&Overlap(b,panelBox))return false;
        for(int j=0;j<obstacles.Length;j++)if(Overlap(b,obstacles[j]))return false;
        for(int j=0;j<icons.Length;j++)
        {
            if(!icons[j].gameObject.activeSelf)continue;
            Vector2 p=Point(j);
            if(Overlap(b,new Vector4(p.x-24,p.y-24,48,48)))return false;
            if(j!=self&&Overlap(b,boxes[j]))return false;
        }
        return true;
    }
    private Vector4 Bounds(RectTransform rect)
    {
        if(rect==null||!rect.gameObject.activeInHierarchy)return Vector4.zero;
        rect.GetWorldCorners(corners);Vector3 a=mapRect.InverseTransformPoint(corners[0]),b=mapRect.InverseTransformPoint(corners[2]);
        return new Vector4(a.x-mapRect.rect.xMin,a.y-mapRect.rect.yMin,b.x-a.x,b.y-a.y);
    }
    private void HideNames(int i)
    {
        names[i].gameObject.SetActive(false);
        icons[i].Find("PerformerNameLeft").gameObject.SetActive(false);
        icons[i].Find("PerformerNameRight").gameObject.SetActive(false);
        icons[i].Find("NameLeader").gameObject.SetActive(false);
        icons[i].Find("NameNumber").gameObject.SetActive(false);
    }
    private void Position(RectTransform rect,Vector4 box)
    {
        rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;
        rect.anchoredPosition=new Vector2(box.x,box.y);rect.sizeDelta=new Vector2(box.z,box.w);
    }
    private void ShowName(int i,int c,Vector4 box)
    {
        bool right=c%2==0;
        var text=icons[i].Find(right?"PerformerNameLeft":"PerformerNameRight").GetComponent<TextMeshProUGUI>();
        text.text=names[i].text;text.gameObject.SetActive(true);
        var rect=text.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);
        Vector2 p=Point(i);rect.sizeDelta=new Vector2(box.z,box.w);rect.anchoredPosition=new Vector2(box.x+box.z*.5f-p.x,box.y+25-p.y);
        if(c>=2)
        {
            var line=(RectTransform)icons[i].Find("NameLeader");Vector2 a=new Vector2(right?22:-22,0),b=new Vector2(right?31:-31,c<4?56:-56),d=b-a;
            line.anchoredPosition=(a+b)*.5f;line.sizeDelta=new Vector2(d.magnitude,1.5f);line.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);line.gameObject.SetActive(true);
        }
    }
}
