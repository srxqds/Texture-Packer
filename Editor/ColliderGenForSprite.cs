/*----------------------------------------------------------------
// Copyright (C) 2015 广州，Lucky Game
//
// 模块名：
// 创建者：D.S.Qiu
// 修改者列表：
// 创建日期：April 09 2016
// 模块描述：
//----------------------------------------------------------------*/

using UnityEngine;
using System.IO;
using UnityEditor;
using Path = System.Collections.Generic.List<UnityEngine.Vector2>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<UnityEngine.Vector2>>;
//Binary:alpha threshold
//Outline:offset and boudary
//GetPath:island or hole
//Cut:merge mini area and 
//
//逆时针为正
//对out参数在传给第二个函数做参数，遍历会很慢！！！！！！
//是Debug引起的，哭

public class ColliderGenForSprite 
{
	public Texture2D tex;

    public Rect rect;

    public GameObject gameObject;
    



    public bool generateTexture = true;
	
	public float pixelsToUnits = 100f; // Pixels to (unity)Units  100 to 1 
	public float pixelOffset = 0.5f;
    public float alphaThreshold = 0f;
    public int outlineOffset = 0;

    public bool[,] binaryImage;
	
	PolygonCollider2D poly;
	
	public float xBounds;
	public float yBounds;
	
	public int islandCount=0;
    public int holeCount = 0;

    private int[,] mask2x2 = { { 0, 1 }, { -1, 0 }, { 0, -1 }, { 1, 0 } };
    private int[,] mask3x3 = { { 0, 1 }, { 1, 1 }, { 1, 0 }, { 1, -1 }, { 0, -1 }, { -1, -1 }, { -1, 0 }, { -1, 1 } };
    private int mask2x2Length = 4;
    private int mask3x3Length = 8;

    private string texturePath;
    private string binarySuffix = "_binary";
    private string outlineSuffix = "_outline";
    private string cutpathSuffix = "_cutpath";

    [MenuItem("Test/ColliderGen")]
    public static void DoTest()
    {
        var texture = Selection.activeObject as Texture2D;
        if (texture != null)
        {
            new ColliderGenForSprite().DoGen(texture);
        }
    }
    
    public void DoGen(Sprite sprite)
    {
        DoGen(sprite.texture,sprite.rect);
    }

    public void DoGen(Texture2D texture)
    {
        Rect rect = new Rect(0,0,texture.width,texture.height);
        DoGen(texture,rect);
    }

    public void DoGen(Texture2D texture, Rect rect)
    {
        this.rect = rect;
        texturePath = AssetDatabase.GetAssetPath(texture);
        TextureImporter textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        textureImporter.isReadable = true;
        textureImporter.npotScale = TextureImporterNPOTScale.None;
        AssetDatabase.ImportAsset(texturePath);
        this.tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        //binary texture
        binaryImage = BinaryImageFromTexture(tex);
        if (generateTexture)
        {
            string binaryImageAssetPath = EditorUtils.AppendFilePathSuffix(texturePath, binarySuffix);
            TextureFromBinaryImage(binaryImage, binaryImageAssetPath);
        }
        FixOnePointLink(binaryImage);
        //outline
        var outlineImage = OutlineFromBinaryImage(binaryImage);
        
        if (generateTexture)
        {
            string outlineImageAssetPath = EditorUtils.AppendFilePathSuffix(texturePath, outlineSuffix);
            TextureFromBinaryImage(outlineImage, outlineImageAssetPath);
        }

        CutMoreConnectLinked(binaryImage,outlineImage);
        CutTwoLoopLinked(binaryImage, outlineImage);
        
        if (generateTexture)
        {
            string binaryImageAssetPath = EditorUtils.AppendFilePathSuffix(texturePath, binarySuffix + "1");
            TextureFromBinaryImage(binaryImage, binaryImageAssetPath);
        }
        if (generateTexture)
        {
            string outlineImageAssetPath = EditorUtils.AppendFilePathSuffix(texturePath, outlineSuffix + "1");
            TextureFromBinaryImage(outlineImage, outlineImageAssetPath);
        }
        //GetPath
        Paths paths = GetPaths(outlineImage);
        Debug.LogError("Path Count:" + paths.Count);
        //Merge Loop
        MergeMultiLoop(paths);
        AssetDatabase.Refresh();

        //cut simplify
    }

    private bool[,] BinaryImageFromTexture(Texture2D t)
    {
        var b = new bool[t.width+2, t.height+2];

        for (int x = 0; x < t.width + 2; x++)
        {
            for (int y = 0; y < t.height + 2; y++)
            {
                b[x, y] = false;
            }
        }
        Color[] colors = t.GetPixels();
        for (int x = 0; x < t.width; x++)
        {
            for (int y = 0; y < t.height; y++)
            {
                // If alpha >0 true then 1 else 0
                b[x+1, y+1] = colors[x + y*t.width].a > alphaThreshold;
                //去掉孤立的一个像素
                if (b[x, y] && x > 1 && y > 1)
                {
                    if (GetConnectedCount(b, x, y,false) == 0)
                        b[x, y] = false;
                }
            }
        }
        return b;
        
    }

    //当前像素为空，然后周围有像素
    private bool[,] OutlineFromBinaryImage(bool[,] b)
    {
        int count = 0;
        int width = b.GetLength(0);
        int height = b.GetLength(1);
        var outline = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                //有像素，就不是边界
                outline[x, y] = false;
                if (!b[x, y])
                {
                    for (int z = 0; z < mask2x2Length; z++)
                    {
                        int i = x + mask2x2[z, 0];
                        int j = y + mask2x2[z, 1];
                        if (i < width && i >= 0 && j < height && j >= 0)
                        {
                           if(b[i,j])
                           {
                               outline[x, y] = true;
                               count ++;
                              break;  
                           }
                        }
                    }
                }
                if (x > 1 && y > 1)
                {
                    CutSingleLinked(b,outline, x-1, y-1);
                }
            }
        }
        return outline;
    }

    //去掉一个像素的单链
    private void CutSingleLinked(bool[,] binary,bool[,] outline,int x,int y)
    {
        if (!outline[x, y])
            return;
        int width = outline.GetLength(0);
        int height = outline.GetLength(1);
        int singleLinkx = 0;
        int singleLinky = 0;
        bool downX = false;
        bool downY = false;
        bool upX = false;
        bool upY = false;
        
        for (int z = 0; z < mask3x3Length; z++)
        {
            int i = x + mask3x3[z, 0];
            int j = y + mask3x3[z, 1];
            if (i < width && i >= 0 && j < height && j >= 0)
            {
                if (outline[i, j])
                {
                    if (i < x)
                        downX = true;
                    if (i > x)
                        upX = true;
                    if (j > y)
                        upY = true;
                    if (j < y)
                        downY = true;
                    if (i == x || j == y)
                    {
                        singleLinkx = i;
                        singleLinky = j;
                    }
                    //跳过相邻的
                    //z++;
                }
            }
        }
        if (!(downX && upX ) && !(upY && downY))
        {
            outline[x, y] = false;
            binary[x, y] = !binary[x, y];
            //还没有遍历的，后面会处理
            if (singleLinkx < x+1 && singleLinky < y+1)
                CutSingleLinked(binary,outline, singleLinkx, singleLinky);
        }
    }
    
    //去掉多连通，有且只有与两个点连接
    //如果多于两个连接点，自身不影响其他节点的连通性
    private void CutMoreConnectLinked(bool[,] binary ,bool[,] outline)
    {
        int width = outline.GetLength(0);
        int height = outline.GetLength(1);
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if(!outline[x,y])
                    continue;
                bool canCut = true;
                for (int z = 0; z < mask3x3Length; z++)
                {
                    int i = x + mask3x3[z, 0];
                    int j = y + mask3x3[z, 1];
                    if (i < width && i >= 0 && j < height && j >= 0)
                    {
                        if (outline[i, j])
                        {
                            if (GetConnectedCount(outline, i, j) <= 2)
                            {
                                canCut = false;
                                break;
                            }
                        }
                    }
                }
                if (canCut)
                {
                    outline[x, y] = false;
                    binary[x, y] = !binary[x, y];
                }
            }
        }
    }

    //周围八个点全部是同类型像素或者是outline则就不是关键路径可删除
    private void CutTwoLoopLinked(bool[,] binary,bool[,] outline)
    {
        int width = outline.GetLength(0);
        int height = outline.GetLength(1);
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool hasBinary = false;
                bool lastBinary = false;
                if (!outline[x, y] || y == height -1 || y == 0 || x == 0 || x== width-1)
                    continue;
                bool canCut = true;
                for (int z = 0; z < mask3x3Length; z++)
                {
                    int i = x + mask3x3[z, 0];
                    int j = y + mask3x3[z, 1];
                    if (i < width && i >= 0 && j < height && j >= 0)
                    {
                        if (!outline[i, j])
                        {
                            if (hasBinary)
                            {
                                //存在两种可能就
                                if (binary[i, j] != lastBinary)
                                {
                                    canCut = false;
                                    break;
                                }
                            }
                            else
                            {
                                lastBinary = binary[i,j];
                            }
                            hasBinary = true;
                        }
                    }
                }
                if (canCut)
                {
                    outline[x, y] = false;
                    binary[x, y] = !binary[x, y];
                }
            }
        }
    }

    //修复 - - 情况 和 
    private void FixOnePointLink(bool[,] binary)
    {
        int width = binary.GetLength(0);
        int height = binary.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!binary[x,y])
                {
                    //方便GetConnectedCount计算
                    binary[x, y] = true;
                    if(GetConnectedCount(binary, x, y) == 2 && GetConnectedCount(binary,x,y,false) == 2
                        || GetConnectedCount(binary, x, y, false) == 0 && GetConnectedCount(binary,x,y) >= 3)
                        binary[x, y] = true;
                    else
                    {
                        //如果不满足就重置回来
                        binary[x, y] = false;
                    }
                }
            }
        }
    }

    private int GetConnectedCount(bool[,] outline, int x, int y,bool useMask3x3 = true)
    {
        if (!outline[x, y])
            return 0;
        int count = 0;
        int width = outline.GetLength(0);
        int height = outline.GetLength(1);
        int length = mask3x3Length;
        int[,] mask = mask3x3;
        if (!useMask3x3)
        {
            length = mask2x2Length;
            mask = mask2x2;
        }
        for (int z = 0; z < length; z++)
        {
            
            int i = x + mask[z, 0];
            int j = y + mask[z, 1];
            
            if (i < width && i >= 0 && j < height && j >= 0)
            {
                if (outline[i, j])
                {
                    count ++;
                }
            }
        }
        return count;
    }
    
    //合并多回路
    private void MergeMultiLoop(Paths paths)
    {
        //todo
    }
    
    private Paths GetPaths(bool[,] outline)
    {
        Paths paths = new Paths();
        Vector2 startPoint = Vector2.zero;
        //记录迭代情况
        bool[,] record = (bool[,])outline.Clone();
        int count = 0;
        while (FindStartPoint(record,ref startPoint))
        {
            // Get vertices from outline
            bool isOpen = true;
            Vector2 currPoint = startPoint;
            Vector2 prePoint = currPoint;
            Vector2 newPoint = Vector2.zero;
            Path prevPoints = new Path();
            int w = outline.GetLength(0); // width
            int h = outline.GetLength(1); // height

            while (isOpen)
            {
                if (prevPoints.Contains(currPoint))
                {
                    Debug.LogError("Repeat add point into the path!" + currPoint);
                    break;
                }
                prePoint = currPoint;
                if (prevPoints.Count > 0)
                    prePoint = prevPoints[prevPoints.Count - 1];
                prevPoints.Add(currPoint);
                // Check each direction around the start point and repeat for each new point
                for (int z = 0; z < mask3x3Length; z++)
                {
                    int i = (int)currPoint.x + mask3x3[z, 0];
                    int j = (int)currPoint.y + mask3x3[z, 1];
                    if (i < w && i >= 0 && j < h && j >= 0)
                    {
                        bool canAccess = record[i, j] || GetConnectedCount(outline, i, j) > 2;
                        if (canAccess)
                        {
                            newPoint = new Vector2(i, j);
                            if (inPath(currPoint, newPoint, paths))
                                continue;
                            if (newPoint == prePoint)
                                continue;
                            int index = prevPoints.IndexOf(newPoint);
                            if (index != -1)
                            {
                                Path newPath = new Path();
                                for (int newi = index; newi < prevPoints.Count; newi++)
                                    newPath.Add(prevPoints[newi]);
                                foreach (Vector2 point in newPath)
                                {
                                    record[(int)point.x, (int)point.y] = false;
                                }
                                paths.Add(newPath);
                                //多个分支
                                if (index != 0)
                                {
                                    prevPoints.RemoveRange(index, prevPoints.Count - index);
                                }
                                else
                                    isOpen = false;
                            }
                            currPoint = newPoint;
                            break;
                        }
                    }
                }
            }
            //防止异常情况死循环，方便调试
            count ++;
            if(count == 1000)
                break;
        }
        return paths;
    }

    // returns true if found a start point
    private bool FindStartPoint(bool[,] b, ref Vector2 startPoint)
    {
        int w = b.GetLength(0); // width
        int h = b.GetLength(1); // height

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (b[x, y])
                {
                    startPoint = new Vector2(x, y);
                    Debug.Log("StartPoint: " + startPoint);
                    return true;
                }
            }
        }
        return false; // Cannot find any start points.
    }
    
    private bool inPath(Vector2 point1, Vector2 point2, Paths paths)
    {
        foreach (var path in paths)
        {
            int index = path.IndexOf(point1);
            if (index != -1)
            {
                if (path[(index + 1)%path.Count] == point2)
                    return true;
            }
        }
        return false;
    }
    
    //output texture from binary array
    public void TextureFromBinaryImage(bool[,] b, string assetPath)
    {
        var t = new Texture2D(b.GetLength(0), b.GetLength(1));
        t.wrapMode = TextureWrapMode.Clamp;

        for (int x = 0; x < t.width; x++)
        {
            for (int y = 0; y < t.height; y++)
            {
                //if(x <t.width/2 && y <t.height/2)
                t.SetPixel(x, y, (b[x, y] ? Color.white : Color.black)); // if true then white else black
            }
        }
        t.Apply();
        File.WriteAllBytes(EditorUtils.AssetPath2FilePath(assetPath), t.EncodeToPNG());
    }

    public Rect GetBounds(Path path)
    {
        Rect rect = new Rect(Vector2.zero,Vector2.zero);
        foreach (var vert in path)
        {
            if (vert.x < rect.xMin)
                rect.xMin = vert.x;
            if (vert.x > rect.xMax)
                rect.xMax = vert.x;
            if (vert.y < rect.yMin)
                rect.yMin = vert.y;
            if (vert.y > rect.yMax)
                rect.yMax = vert.y;
        }
        return rect;
    }

    //在y轴线段在点的两边
    public bool PointInPath(Vector2 point, Path path)
    {
        bool result = false;
        int j = path.Count - 1;
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].y < point.y && path[j].y >= point.y 
                || path[j].y < point.y && path[i].y >= point.y)
            {
                if (path[i].x + (point.y - path[i].y) / (path[j].y - path[i].y) * (path[j].x - path[i].x) < point.x)
                {
                    result = !result;
                }
            }
            j = i;
        }
        return result;
    }
    
}
