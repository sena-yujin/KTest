using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Newtonsoft.Json;

[System.Serializable]
public class Row
{
    [JsonProperty("ID")]public string id;
    [JsonProperty("NAME")] public string name;
    [JsonProperty("RARITY")]public string rarity;
    [JsonProperty("ARTKEY")]public string artkey;
    [JsonProperty("BANNER")]public string banner;
    [JsonProperty("IMAGEURL")]public string imageUrl;

}

[System.Serializable]
public class Response
{
    public bool ok;
    public string error;
    public List<Row> rows;
    public string updatedAt;
}


public class GachaController : MonoBehaviour
{
    private string webAppUrl = "https://script.google.com/macros/s/AKfycbxiblLgn-_sErAHFMUIKkwy102RXadNEyYByejFv1Pfig6vFqK7loiToijbOzqmjhVB/exec";
    private string token = "QWERTYUIOP";
    private string sheet = "sheet1";
    private int maxcell = 10;
    private int requestTimeoutSec = 10;

    public UIDocument uiDocument;
    private string gallereyContainerName = "gallery";
    private string cellClass = "gallery-cell";
    private string imageClass = "img";
    private bool addTitleLabel = true;

    private Coroutine cor;
    private VisualElement _gallery;

    public List<Row> rows = new();

    private static readonly Dictionary<string, int> Rarity_Weight = new()
    {
        { "N", 30 },
        { "R", 60 },
        { "SR", 9 },
        { "SSR", 1 }

    };


    private void Awake()
    {
      //  if(uiDocument==null)
      //  {
      //      enabled = false;
      //      return;
      //  }
        var root = uiDocument.rootVisualElement;
        _gallery = root.Q(gallereyContainerName);
      //  if(_gallery==null)
      //  {
      //      enabled = false;
      //  }

    }

    void Start()
    {
        cor = StartCoroutine(LoadAndBuild());
        
    }

    private IEnumerator LoadAndBuild()
    {
        //1) get JSON 
        var url = $"{webAppUrl}?token={UnityWebRequest.EscapeURL(token)}&sheet={UnityWebRequest.EscapeURL(sheet)}";
        Response resp = null;

        using (var req=UnityWebRequest.Get(url))
        {
            req.timeout = requestTimeoutSec;
            yield return req.SendWebRequest();

            if(req.result !=UnityWebRequest.Result.Success)
            {
                Debug.Log(req.error);
                yield break;
            }

            try
            {
            resp = JsonConvert.DeserializeObject<Response>(req.downloadHandler.text);

            }
            catch(Exception ex)
            {
                Debug.Log($"fail to JSON pharsing : {ex.Message}");
                yield break;
            }
        }

            if(resp==null || !resp.ok)
            {
                Debug.Log($"API error : {resp?.error }");
                yield break;
            
            }

            var all = resp.rows ?? new List<Row>();
            Debug.Log($"Loaded {rows.Count} rows. UpdatedAt={resp.updatedAt}");

        //Exception handling for blank cells in spreadsheet
        var candidates = all.FindAll(HasAllRequiredInSpreadSheet);

        if(candidates.Count==0)
        {

            Debug.Log($"Loaded {rows.Count} rows. UpdatedAt={resp.updatedAt}");
            yield break;
        }

        //pool by grade
        var pools = BuildPoolsByRarity(candidates);

        var rng = new System.Random();
        var picked = new List<Row>(maxcell);
        for(int i=0; i<maxcell;i++)
        {
            var one = DrawOneByRarity(pools,rng);
            if (one == null) break;
            picked.Add(one);
        }

        // set gallery
        _gallery.Clear();

        for(int i=0; i<picked.Count;i++)
        {
            var cell = new VisualElement();
            cell.AddToClassList(cellClass);

            var img = new Image();
            img.AddToClassList(imageClass);
            cell.Add(img);

            _gallery.Add(cell);

            yield return StartCoroutine(LoadTextureToImage(picked[i].imageUrl,img));
        }

    }

    private static bool HasAllRequiredInSpreadSheet(Row r)
    {
        return
            !string.IsNullOrWhiteSpace(r.id) &&
            !string.IsNullOrWhiteSpace(r.name) &&
            !string.IsNullOrWhiteSpace(r.rarity) &&
            !string.IsNullOrWhiteSpace(r.artkey) &&
            !string.IsNullOrWhiteSpace(r.banner) &&
            !string.IsNullOrWhiteSpace(r.imageUrl);

    }

  //  private static void ShuffleInPlane<T>(IList<T> list)
  //  {
  //      var rng = new System.Random();
  //      for(int i=list.Count-1;i>0;i--)
  //      {
  //          int j = rng.Next(0,i+1);
  //          (list[i],list[j]) = (list[j],list[i]);
  //      }
  //  }

    private IEnumerator LoadTextureToImage(string url, Image target)
    {
        if (string.IsNullOrWhiteSpace(url) || target==null)
            yield break;

        using (var req=UnityWebRequestTexture.GetTexture(url))
        {
            req.timeout = requestTimeoutSec;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var tex = DownloadHandlerTexture.GetContent(req);
                target.image = tex;
            }
            else Debug.Log($"image load fail : {url}-{req.error}");

        }
    }

    private static Dictionary<string,List<Row>> BuildPoolsByRarity(List<Row> rows)
    {
        var dict = new Dictionary<string, List<Row>>(StringComparer.OrdinalIgnoreCase); //??

        foreach (var kv in Rarity_Weight.Keys)
        {
            dict[kv] = new List<Row>();
        }

        foreach(var r in rows)
        {
            var key = (r.rarity ?? "").Trim().ToUpperInvariant(); //?? 그냥 r.rarity 해도 될거같은데
            if (dict.ContainsKey(key)) dict[key].Add(r);
        }
        return dict;

    }
     
    private static Row DrawOneByRarity(Dictionary<string,List<Row>> pools, System.Random ran)
    {
        var active = new List<(string rarity, int weight)>();
        foreach(var kv in Rarity_Weight)
        {
            if (pools.TryGetValue(kv.Key, out var _list) && _list.Count > 0)
                active.Add((kv.Key,kv.Value));
        }
        if (active.Count == 0) return null;

        int total = 0;
        foreach (var a in active) total += a.weight;
        int roll = ran.Next(1,total+1);

        string chosen = null;
        int acc = 0;
        foreach(var a in active)
        {
            acc += a.weight;
            if (roll <= acc) { chosen = a.rarity; break; }

        }

        var list = pools[chosen];
        return list[ran.Next(list.Count)];

    }

}
