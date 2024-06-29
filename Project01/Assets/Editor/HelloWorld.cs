using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;



// 因为Assets下的其他脚本会被编译到AssetmblyCharp.dll中，跟随着包体打包出去(如APK），
// 所以不允许使用来自UnityEditor命名空间下的方法
public class HelloWorld : MonoBehaviour
{

    public AssetBundlePattern LoadPattern;

    public Button LoadBundleButton;
    public Button LoadAssetButton;
    public Button UnLoadFalseButton;
    public Button UnLoadTrueButton;

    AssetBundle CubeBundle;
    AssetBundle SphereBundle;
    GameObject CubeObject; // 保存克隆出来的物体以便销毁
    GameObject SphereObject;

    // 打包的包名本应该由Editor类管理，但因为资源加载类也需要访问，所有放在资源加载类中
    //public static string MainAssetBundleName = "SampleAssetBundle"; // 打包出来的文件名称
    //public static string ObjectAssetBundleName = "resourcesbundle";

    public string AssetBundleLoadPath;

    // 将文件放到HPS中，获取的路径
    public string HTTPAddress = "http://192.168.8.83:8080/";

    public string HTTPAssetBundlePath;

    public string DownloadPath;

    void Start()
    {

        AssetManagerRuntime.AssetManagerInit(LoadPattern);
        AssetPackage assetPackage = AssetManagerRuntime.Instance.LoadPackage("A");
        Debug.Log(assetPackage.PackageName);


        GameObject sampleObject = assetPackage.LoadAsset<GameObject>("Assets/SampleAssets/Cube.prefab");
        Instantiate(sampleObject);


    //    CheckAssetBundleLoadPath();

    //    // 绑定点击事件
    //    LoadBundleButton.onClick.AddListener(CheckAssetBundlePattern);
    //    LoadAssetButton.onClick.AddListener(LoadAsset);
    //    UnLoadFalseButton.onClick.AddListener(() => { UnLoadAsset(false); });
    //    UnLoadTrueButton.onClick.AddListener(() => { UnLoadAsset(true); });
    }

    void CheckAssetBundleLoadPath() // 检查包是哪种路径
    {
        switch (LoadPattern) 
        {
            case AssetBundlePattern.EditorSimulation:
                break;
            case AssetBundlePattern.Local:
                AssetBundleLoadPath = Path.Combine(Application.streamingAssetsPath);
                break;
            case AssetBundlePattern.Remote:
                // AB存放的文件夹路径
                HTTPAssetBundlePath = Path.Combine(HTTPAddress);

                // 下载的AB包文件夹路径：AssetBundleLoadPath
                DownloadPath = Path.Combine(Application.persistentDataPath, "DownloadAssetBundle");
                AssetBundleLoadPath = Path.Combine(DownloadPath);

                if (!Directory.Exists(AssetBundleLoadPath))
                {
                    Directory.CreateDirectory(AssetBundleLoadPath);
                }
                break;
        }

    }

    // 从远端下载AB包
    IEnumerator DownloadFile(string fileName, Action callBack, bool isSaveFile = true)
    {
        // 远端AB包的文件夹路径
        string assetBundleDownloadPath = Path.Combine(HTTPAddress, fileName);

        // HPS中的名为SampleAssetBundle包的路径http://192.168.8.83:8080/SampleAssetBundle/SampleAssetBundle
        Debug.Log($"AB包从该路径下载：{assetBundleDownloadPath}");
        UnityWebRequest webRequest = UnityWebRequest.Get(assetBundleDownloadPath);
        
        yield return webRequest.SendWebRequest();

        while (!webRequest.isDone)
        {
            Debug.Log(webRequest.downloadedBytes); // 下载总字节数
            Debug.Log(webRequest.downloadProgress); // 下载进度
            yield return new WaitForEndOfFrame();
        }

        // AB包的路径
        string fileSavePath = Path.Combine(AssetBundleLoadPath, fileName);

        //C:/Users/wyh/AppData/LocalLow/DefaultCompany/Test\DownloadAssetBundle
        //                              \SampleAssetBundle\SampleAssetBundle
        Debug.Log($"AB包保存的地址为：{fileSavePath}");
        Debug.Log(webRequest.downloadHandler.data.Length);
        if (isSaveFile)
        {
            yield return SaveFile(fileSavePath, webRequest.downloadHandler.data, callBack);
        }
        else
        {
            // 三目运算符判断对象是否为空
            callBack?.Invoke();
        }
    }

    // 将远程的文件写入到本地
    IEnumerator SaveFile(string savePath,byte[] bytes, Action callBack)
    {
        FileStream fileStream = File.Open(savePath, FileMode.OpenOrCreate);

        yield return fileStream.WriteAsync(bytes, 0, bytes.Length);

        // 释放文件流，否则文件会一直处于读取的状态而不能被其他进程读取
        fileStream.Flush();
        fileStream.Close();
        fileStream.Dispose();

        callBack?.Invoke();
        Debug.Log($"{savePath}文件保存完成");

    }

    void CheckAssetBundlePattern() // 远程remote
    {
        if (LoadPattern == AssetBundlePattern.Remote)
        {
            StartCoroutine(DownloadFile("", LoadAssetBundle));
        }
        else
        {
            LoadAssetBundle();
        }
    }

    // 点击按钮，加载AB包
    void LoadAssetBundle()
    {
        // 加载AB包的路径，与上面下载AB包的路径一致
        string assetBundlePath = Path.Combine(AssetBundleLoadPath, "");
        Debug.Log($"加载AB包的路径为：{assetBundlePath}");
        // C:/Users/wyh/AppData/LocalLow/DefaultCompany/Test\DownloadAssetBundle
        //                                 \SampleAssetBundle\SampleAssetBundle
        
        // 通过清单捆绑包(主包)的Manifest,来查找依赖
        AssetBundle mainAB = AssetBundle.LoadFromFile(assetBundlePath); // 从文件夹中加载AB包
        
        // manifest文件本身实际上是明文存储给我们开发者查找索引的
        AssetBundleManifest mainManifest = mainAB.LoadAsset<AssetBundleManifest>(nameof(AssetBundleManifest));

        // 在实例化对象之前,将所有依赖包都加载到内存中
        // 从 .manifest中获取assetBundleName的依赖，名字与AB包的包名相同，此次名称为0
        foreach (var depBundleName in mainManifest.GetAllDependencies("0"))
        {
            // 获取 Assets/ ...\SampleAssetBundle\依赖包名称，杠的方向还不一样
            assetBundlePath = Path.Combine(AssetBundleLoadPath, depBundleName);

            Debug.Log(assetBundlePath);
            AssetBundle.LoadFromFile(assetBundlePath); // 因为只是依赖项，所以不用变量储存
            
        }

        // 从文件夹中获取AB包，传入AB包的包名为 0 (需要克隆的物体的AB名)
        assetBundlePath = Path.Combine(AssetBundleLoadPath, "0");
        CubeBundle = AssetBundle.LoadFromFile(assetBundlePath);

        assetBundlePath = Path.Combine(AssetBundleLoadPath, "1");
        SphereBundle = AssetBundle.LoadFromFile(assetBundlePath); 
    }

    // 点击按钮，加载资源
    void LoadAsset()
    {
        // 从AB包中加载资源对象，并实例化, 预制体的名称为 Cube
        GameObject cubeObject = CubeBundle.LoadAsset<GameObject>("Cube");
        CubeObject = Instantiate(cubeObject); // 实例化物体

        GameObject sphereObject = SphereBundle.LoadAsset<GameObject>("Sphere");
        SphereObject = Instantiate(sphereObject); // 实例化物体
    }

    // 点击按钮，卸载资源
    void UnLoadAsset(bool isTrue)
    {
        // unload(true)时，会强制从内存中回收资源类型对象，但无法回收游戏对象
        if (isTrue) // isTrue为true时，执行
        {
            // 强制回收AssetBundle包所有的资源，但实例化的物体还存在，只不过引用丢失了
            CubeBundle.Unload(isTrue); // true
            SphereBundle.Unload(isTrue);
        }
        else
        {
            // 删除场景中的实例以减少对资源的引用
            DestroyImmediate(CubeObject); // 销毁物体
            DestroyImmediate(SphereObject);

            CubeBundle.Unload(isTrue); // false
            SphereBundle.Unload(isTrue);

            // 从内存中卸载从未使用过的资源
            Resources.UnloadUnusedAssets();
        }
    }
}
