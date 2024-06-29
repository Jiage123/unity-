using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

[Serializable]
public class PackageEditorInfo
{
    // 当前包的名称可以由开发者在编辑器窗口中自由定义
    public string PackageName;

    // 归属于当前包中的资源列表，可以由开发者在编辑器中自由定义
    public List<UnityEngine.Object> AssetList = new List<UnityEngine.Object>();
}

public class AssetManagerEditor
{
    public class AssetBundleEdge
    {
        // 引用或被引用的Nodes
        public List<AssetBundleNode> Nodes = new List<AssetBundleNode>();
    }

    public class AssetBundleNode
    {
        // 当前Node所代表的资源名称，代表该资源的唯一性，可以使用GUID代替
        public string AssetName;

        // Node的索引，SourceAsset >= 0
        public int SourceIndex = -1;

        // 依赖该资源的所有SourceAsset的Index，SourceIndices穿透每一层依赖关系体现出依赖
        public List<int> SourceIndices = new List<int>();

        // 只有SourceAsset才具有包名
        public string PackageName;

        // DerivedAsset的只有PackageNames代表别引用关系
        public List<string> PackageNames = new List<string>();

        public AssetBundleEdge InEdge; // 引用该资源的节点，只体现出一层依赖关系
        public AssetBundleEdge OutEdge;   // 该资源引用的节点，只体现出一层依赖关系
    }

    public class AssetBundleVersionDifference
    {
        public List<string> AddtionAssetBundles;  // 本次版本对比中增加的AssetBundle
        public List<string> ReducesAssetBundels;  // 本次版本对比中减少的AssetBundle
    }

    // AB包的输出路径
    public static string AssetBundleOutputPath;

    public static AssetManagerConfigScriptableObject AssetManagerConfig;
    public static AssetManagerEditorWindowConfig AssetManagerWindowConfig;

    // 因为Assets下的其他脚本会被编译到AssetmblyCharp.dll中跟随在包体打包出去，
    // 所以不允许使用来自UnityEditor命名空间下的方法,即不能继承MonoBehaviour
    [MenuItem(nameof(AssetManagerEditor) + "/" + nameof(BuildAssetBundle))]
    // 等于[MenuItem("AssetManager/BuildAssetBundle")]
    static void BuildAssetBundle()
    {
        CheckBuildOutputPath();

        // Directory类用于PC平台对目录进行操作的类
        if (!Directory.Exists(AssetBundleOutputPath))
        {
            Directory.CreateDirectory(AssetBundleOutputPath);
        }

        // 不同平台之间的AssetBundle不可以通用，该方法会打包工程内所有配置了包名的AB，参数：输出目录，打包格式，打包平台
        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows);
        AssetDatabase.Refresh(); // 刷新
        Debug.Log($"AB包打包完成，储存路径为：{AssetBundleOutputPath}");
    }

    // 利用MenuItem特性声明一个静态方法,以创建Window类
    [MenuItem("AssetManagerEditor/OpenAssetManagerWindow")]
    static void OpenAssetManagerEditorWindow()
    {
        // 点击时执行该方法(不用绑定),创建一个窗口，设置一个名为AssetManagerName的窗口
        AssetManagerEditorWindow window = EditorWindow.GetWindow<AssetManagerEditorWindow>("AssetManager");
    }

    // 通过静态方法来创建ScriptableObject文件
    [MenuItem("AssetManagerEditor/CreateConfigFile")]
    public static void CreateNewConfigScriptableObject()
    {
        // 声明ScriptableObject 类型的实例，就类似与JSON中将某个类的实例化的过程
        AssetManagerConfigScriptableObject assetManagerConfig = ScriptableObject.CreateInstance<AssetManagerConfigScriptableObject>();
        AssetDatabase.CreateAsset(assetManagerConfig, "Assets/Editor/AssetManagerConfig.asset");
        AssetDatabase.SaveAssets();  // 保存资源
        AssetDatabase.Refresh(); // 刷新本地目录
    }

    // 检查是哪个打包模式
    public static string OutputBundleName;
    static void CheckBuildOutputPath()
    {
        switch (AssetManagerConfig.BuildingPattern)
        {
            case AssetBundlePattern.EditorSimulation:
                OutputBundleName = "Editor";
                break;
            case AssetBundlePattern.Local:
                OutputBundleName = "Local";
                AssetBundleOutputPath = Path.Combine(Application.streamingAssetsPath, "LocalAssets");
                break;
            case AssetBundlePattern.Remote:
                OutputBundleName = "Remote";
                AssetBundleOutputPath = Path.Combine(Application.persistentDataPath, "LocalAssets");
                break;
        }

        if (!Directory.Exists(AssetBundleOutputPath))
        {
            Directory.CreateDirectory(AssetBundleOutputPath);
            AssetDatabase.Refresh();
        }
    }

    static BuildAssetBundleOptions CheckCompressionPattern()
    {
        BuildAssetBundleOptions option = new BuildAssetBundleOptions();
        switch (AssetManagerConfig.CompressionPattern)
        {
            case AssetBundleCompressionPattern.LZMA:
                option = BuildAssetBundleOptions.None;
                break;
            case AssetBundleCompressionPattern.LZ4:
                option = BuildAssetBundleOptions.ChunkBasedCompression;
                break;
            case AssetBundleCompressionPattern.None:
                option = BuildAssetBundleOptions.UncompressedAssetBundle;
                break;
        }
        return option;
    }

    static BuildAssetBundleOptions CheckIncrementalMode()
    {
        BuildAssetBundleOptions options = BuildAssetBundleOptions.None;

        switch (AssetManagerConfig.BuildMode)
        {
            case IncrementalBuildMode.None:
                options = BuildAssetBundleOptions.None;
                break;
            case IncrementalBuildMode.UseIncrementalBuild:
                options = BuildAssetBundleOptions.DeterministicAssetBundle;
                break;
            case IncrementalBuildMode.ForceRebuild:
                options = BuildAssetBundleOptions.ForceRebuildAssetBundle;
                break;
        }
        return options;
    }

    // 从当前选择资源名数组获取依赖资源名的方法
    public static string[] GetDependenciesFromAsset(string assetPath)
    {
        // 通过该方法获取的所有数组，都可以视为集合列表L中的某个元素，即包含了SourceAsset A以及其衍生资源名称的集合
        // 一旦确定了 Source Asset，调用Unity内的API，获取其依赖的 Derived Asset。
        string[] assetDependecies = AssetDatabase.GetDependencies(assetPath, true);
        foreach (string depName in assetDependecies)
        {
            Debug.Log($"依赖包名称为：{depName}");
        }
        return assetDependecies;
    }

    // 打包指定文件夹下选中的资源，消除冗余(将共同引用的资源，单独保存)
    public static void BuildAssetBundleFromSets()
    {
        CheckBuildOutputPath();

        // 在窗口中被选中将要打包的资源列表 SourceAsset列表A
        List<string> selectedAssets = new List<string>();

        // 所有SourceAsset所依赖的，都视为DerivedAsset，由DerivedAsset的集合所构成的列表
        List<List<GUID>> selectedAssetsDependencies = new List<List<GUID>>();

        // 遍历所有选择的SourceAsset以及依赖，获取集合列表L
        foreach (string assetName in selectedAssets)
        {
            string[] depAssets = GetDependenciesFromAsset(assetName);
            List<GUID> depAssetsGUID = new List<GUID>();
            foreach (string depName in depAssets)
            {
                GUID assetGUID = AssetDatabase.GUIDFromAssetPath(depName);
                depAssetsGUID.Add(assetGUID);
            }
            // 将当前资源的依赖集合存入列表L
            selectedAssetsDependencies.Add(depAssetsGUID);
        }

        // 对比任意的集合元素i和i+1
        for (int i = 0; i < selectedAssetsDependencies.Count; i++)
        {
            int nextCount = i + 1;
            if ((nextCount >= selectedAssetsDependencies.Count))
            {
                break;
            }

            for (int j = 0; j <= i; j++)
            {
                List<GUID> newDerivedAssets = ContrastDependenciesFromGUID(selectedAssetsDependencies[j],
                                             selectedAssetsDependencies[nextCount]);

                // 将Snew添加到集合列表L中，继续对比
                if (newDerivedAssets.Count > 0)
                {
                    selectedAssetsDependencies.Add(newDerivedAssets);
                }
            }
        }

        // 数组的长度代表打包AB包的数量
        AssetBundleBuild[] assetBundleBuilds = new AssetBundleBuild[selectedAssetsDependencies.Count];

        for (int i = 0; i < selectedAssetsDependencies.Count; i++)
        {
            string[] assetNames = new string[selectedAssetsDependencies[i].Count];
            assetBundleBuilds[i].assetBundleName = i.ToString(); // 打包出来的包名
            for (int j = 0; j < assetNames.Length; j++)
            {
                string assetName = AssetDatabase.GUIDToAssetPath(selectedAssetsDependencies[i][j]);
                assetNames[j] = assetName;  // 获取当前集合中第J个元素的资源路径
            }
            assetBundleBuilds[i].assetNames = assetNames; // 单个AB包中只包含要打包的资源
        }

        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, assetBundleBuilds, BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows);
        Debug.Log($"AB包打包完成，储存路径为：{AssetBundleOutputPath}");
        AssetDatabase.Refresh();   // 刷新Project界面，如果不是打包到工程内则不需要执行
    }

    public static List<GUID> ContrastDependenciesFromGUID(List<GUID> depA, List<GUID> depB)
    {
        // 第三步中所需要Snew集合，作为返回值返回
        List<GUID> newDerivedAssets = new List<GUID>();

        // 取交集
        foreach (GUID asset in depA)
        {
            if (depB.Contains(asset))
            {
                newDerivedAssets.Add(asset);
            }
        }

        // 取差集，第三步所提到的，从Si和Sj中取与Snew的差集
        foreach (GUID asset in newDerivedAssets)
        {
            if (depA.Contains(asset))
            {
                depA.Remove(asset);
            }
            if (depB.Contains(asset))
            {
                depB.Remove(asset);
            }
        }
        Debug.Log(newDerivedAssets.Count);
        return newDerivedAssets;
    }

    // 打包指定文件夹下选中的资源
    public static void BuildAssetBundleFromEditorWindow()
    {
        CheckBuildOutputPath();

        // 选中了多少资源，则打包多少个AB包
        List<string> selectedAssets = new List<string>();
        // 数组的长度代表打包AB包的数量
        AssetBundleBuild[] builds = new AssetBundleBuild[selectedAssets.Count];
        for (int i = 0; i < selectedAssets.Count; i++)
        {
            //string directoryPath = AssetDatabase.GetAssetPath(AssetManagerConfig.AssetBundleDirectory);
            //从带有Assets目录的资源名称，替换掉目录部分
            //string assetName = selectedAssets[i].Replace($@"{directoryPath}\", string.Empty);
            //assetName = assetName.Replace(".prefab", string.Empty);

            //builds[i].assetBundleName = assetName; // 使用文件名作为资源包名
            builds[i].assetNames = new string[] { selectedAssets[i] };  // 单个AB包中只包含要打包的资源
        }

        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, builds, BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows);
        Debug.Log($"AB包打包完成，储存路径为：{AssetBundleOutputPath}");
        AssetDatabase.Refresh();
    }

    // 打包指定文件夹下所有资源为AssetBundle
    public static void BuildAssetBundleFromDirectory()
    {
        CheckBuildOutputPath();

        // 获取拖入窗口中的所有资源
        //AssetManagerConfig.GetCurrentAllAssetsFromDirectory();

        // 数组的长度代表打包AB包的数量
        AssetBundleBuild[] assetBundleBuilds = new AssetBundleBuild[1];

        // 设置打包出来的AB包名称为resource.ab
        assetBundleBuilds[0].assetBundleName = "Local";
        //assetBundleBuilds[0].assetNames = AssetManagerConfig.CurrentAllAssets.ToArray();

        if (string.IsNullOrEmpty(AssetBundleOutputPath))
        {
            Debug.LogError("输出路径为空");
            return;
        }
        else if (!Directory.Exists(AssetBundleOutputPath))
        {
            Directory.CreateDirectory(AssetBundleOutputPath);
        }

        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, assetBundleBuilds, BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows);
        Debug.Log($"AB包打包完成，储存路径为：{AssetBundleOutputPath}");
        AssetDatabase.Refresh();
    }

    // 构建有向图，其实allNodes 可以用成员变量来储存，就不需要再函数中传递了
    static void BuildDirectedGraph(AssetBundleNode lastNode, List<AssetBundleNode> allNodes)
    {
        if (lastNode == null)
        {
            Debug.Log("lastNode为空，构建有向图失败");
        }
        // 只获取直接的依赖
        string[] depends = AssetDatabase.GetDependencies(lastNode.AssetName, false);

        // 依赖的资源数量等于0，代表已经走到了引用关系的最终点，也就是有向图的终点，于是向上返回
        if (depends.Length <= 0)
        {
            return;
        }

        // OutEdge为空代表没有新增过依赖，但是depends > 0 代表肯定有依赖的资源
        if (lastNode.OutEdge == null)
        {
            lastNode.OutEdge = new AssetBundleEdge();
        }

        // 一个资源所直接依赖的资源是有限的，视为x
        foreach (var dependName in depends)
        {

            // 每一个有效的资源都作为一个新的Node
            AssetBundleNode currentNode = null;

            // 而已经存在的Node是一个比较大的值，视为n；有向图构建过程中，嵌套层数为 y
            // 最大的可能的遍历次数为 y*x*n，所以实际上的遍历次数肯定低于n*n
            foreach (AssetBundleNode existingNode in allNodes)
            {
                // 如果资源已经创建节点，那么直接引用已有节点
                if (existingNode.AssetName == dependName)
                {
                    currentNode = existingNode;
                    break;
                }
            }

            // 如果不是已有节点，则新增一个新节点
            if (currentNode == null)
            {
                currentNode = new AssetBundleNode();
                currentNode.AssetName = dependName;

                // 因为当前Node必定是LastNode的依赖对象，所以必然存在InEdge和SourceIndices
                currentNode.InEdge = new AssetBundleEdge();
                currentNode.SourceIndices = new List<int>();
                allNodes.Add(currentNode);
            }

            currentNode.InEdge.Nodes.Add(lastNode);
            lastNode.OutEdge.Nodes.Add(currentNode);

            // 包名以及包对资源的引用，同样也通过有向图进行传递
            if (!string.IsNullOrEmpty(lastNode.PackageName))
            {
                if (!currentNode.PackageNames.Contains(lastNode.PackageName))
                {
                    currentNode.PackageNames.Add(lastNode.PackageName);
                }

            }
            else // 否则是DerivedAsset,直接获取lastNOde的SourceIndices即可
            {
                foreach (string packageNames in lastNode.PackageNames)
                {
                    if (!currentNode.PackageNames.Contains(packageNames))
                    {
                        currentNode.PackageNames.Add(packageNames);
                    }
                }
            }

            // 如果lastNode是SourceAsset，则直接为当前的Node添加lastNode的Index
            // 因为List是一个引用类型，所有SourceAsset的SourceIndices哪怕和内容和Drived一样，也视为一个新的List
            if (lastNode.SourceIndex >= 0)
            {
                if (!currentNode.SourceIndices.Contains(lastNode.SourceIndex))
                {
                    currentNode.SourceIndices.Add(lastNode.SourceIndex);
                }

            }
            else // DerivedAsset,直接获取lastNOde的SourceIndices即可
            {
                foreach (int index in lastNode.SourceIndices)
                {
                    if (!currentNode.SourceIndices.Contains(index))
                    {
                        currentNode.SourceIndices.Add(index);
                    }
                }
            }
            BuildDirectedGraph(currentNode, allNodes);
        }
    }

    // 从有向图中构建AB包
    public static void BuildAssetBundleFromDirectedGraph()
    {
        CheckBuildOutputPath();

        List<AssetBundleNode> allNodes = new List<AssetBundleNode>();
        int sourceIndex = 0;
        Dictionary<string, PackageBuildInfo> packageInfoDic = new Dictionary<string, PackageBuildInfo>();

        #region 有向图构建

        for (int i = 0; i < AssetManagerConfig.packageInfoEditors.Count; i++)
        {
            PackageBuildInfo packageBuildInfo = new PackageBuildInfo();
            packageBuildInfo.PackageName = AssetManagerConfig.packageInfoEditors[i].PackageName;
            packageBuildInfo.IsSourcePackage = true;
            packageInfoDic.Add(packageBuildInfo.PackageName, packageBuildInfo);

            // 当前所选中的资源，就是SourceAsset,所以首先添加SourceAsset的Node
            foreach (UnityEngine.Object asset in AssetManagerConfig.packageInfoEditors[i].AssetList)
            {
                AssetBundleNode currentNode = null;

                // 以资源的具体路径，作为资源名称
                string assetNamePath = AssetDatabase.GetAssetPath(asset);

                foreach (AssetBundleNode node in allNodes)
                {
                    if (node.AssetName == assetNamePath)
                    {
                        currentNode = node;
                        currentNode.PackageName = packageBuildInfo.PackageName;
                        break;
                    }
                }

                if (currentNode == null)
                {
                    currentNode = new AssetBundleNode();
                    currentNode.AssetName = assetNamePath;

                    // 为什么SourceAsset具有了SourceIndex还需要使用SourceIndices？
                    // 这是因为可以使得OutEdge直接使用SourceAsset的SourceIndices
                    currentNode.SourceIndex = sourceIndex;
                    currentNode.SourceIndices = new List<int>() { sourceIndex };

                    currentNode.PackageName = packageBuildInfo.PackageName;
                    currentNode.PackageNames.Add(currentNode.PackageName);

                    currentNode.InEdge = new AssetBundleEdge();
                    allNodes.Add(currentNode);
                }

                BuildDirectedGraph(currentNode, allNodes);

                sourceIndex++;
            }
        }
        #endregion

        #region 有向图分区打包
        // key代表SourceIndices，key相同的Node，应该添加到同一个集合中
        Dictionary<List<int>, List<AssetBundleNode>> assetBundleNodesDic = new Dictionary<List<int>, List<AssetBundleNode>>();

        foreach (AssetBundleNode node in allNodes)
        {
            StringBuilder packageNameString = new StringBuilder();

            // 包名不为空或无，则代表是一个SourceAsset，其包名已经在编辑器窗口中添加了
            if (string.IsNullOrEmpty(node.PackageName))
            {
                for (int i = 0; i < node.PackageNames.Count; i++)
                {
                    packageNameString.Append(node.PackageNames[i]);
                    if (i < node.PackageNames.Count - 1)
                    {
                        packageNameString.Append("_");
                    }
                }

                string packageName = packageNameString.ToString();
                node.PackageName = packageName;

                // 此时只添加了对应的包以及包名，而没有具体添加包中对应的Asset
                // 因为Asset添加是需要具有AssetBundleName，所有只能在生成AssetBundleBuild的地方添加Asset
                if (!packageInfoDic.ContainsKey(packageName))
                {
                    PackageBuildInfo packageBuildInfo = new PackageBuildInfo();
                    packageBuildInfo.PackageName = packageName;
                    packageBuildInfo.IsSourcePackage = false;
                    packageInfoDic.Add(packageBuildInfo.PackageName, packageBuildInfo);
                }
            }

            bool isEquals = false;
            List<int> keyList = new List<int>();

            // 遍历所有的key，通过这样的方式就能确保，不同的List之间，内容是一致的
            foreach (List<int> key in assetBundleNodesDic.Keys)
            {
                // 判断key的长度是否和当前node的SourceIndices的长度相等
                isEquals = node.SourceIndices.Count == key.Count && node.SourceIndices.All(p => key.Any(k => k.Equals(p)));

                if (isEquals)
                {
                    keyList = key;
                    break;
                }
            }
            if (!isEquals)
            {
                keyList = node.SourceIndices;
                assetBundleNodesDic.Add(node.SourceIndices, new List<AssetBundleNode>());
            }

            // Node在构建时就能保证肯定不会重复
            assetBundleNodesDic[keyList].Add(node);
        }
        #endregion

        AssetBundleBuild[] assetBundleBuilds = new AssetBundleBuild[assetBundleNodesDic.Count];
        int buildIndex = 0;
        foreach (var key in assetBundleNodesDic.Keys)
        {
            assetBundleBuilds[buildIndex].assetBundleName = buildIndex.ToString();
            List<string> assetNames = new List<string>();
            foreach (var node in assetBundleNodesDic[key])
            {
                assetNames.Add(node.AssetName);
                //Debug.Log($"Key值的长度={key.Count}，具有Node：{node.AssetName}");

                // 如果一个SourceAsset，则它的PackageName只会具有自己
                foreach (string packageName in node.PackageNames)
                {
                    if (packageInfoDic.ContainsKey(packageName))
                    {
                        if (!packageInfoDic[packageName].PackageDependecies.Contains(node.PackageName) &&
                            !string.Equals(node.PackageName, packageInfoDic[packageName].PackageName))
                        {
                            packageInfoDic[packageName].PackageDependecies.Add(node.PackageName);
                        }
                    }
                }
            }

            // 传入的参数是每一个AssetBundle中所有参与打包的Asset路径
            // 如果参与打包的Asset路径没有发生改变，则代表该包没有更新内容
            assetBundleBuilds[buildIndex].assetBundleName = ComputeAssetSetSignature(assetNames);
            assetBundleBuilds[buildIndex].assetNames = assetNames.ToArray();

            foreach (AssetBundleNode node in assetBundleNodesDic[key])
            {

                // 因为区分了的DerivedPackage，所以此处可以确保，每一个Node都具有一个包名
                AssetBuildInfo assetBuildInfo = new AssetBuildInfo();

                assetBuildInfo.AssetName = node.AssetName;
                assetBuildInfo.AssetBundleName = assetBundleBuilds[buildIndex].assetBundleName;

                packageInfoDic[node.PackageName].AssetInfos.Add(assetBuildInfo);

            }
            buildIndex++;
        }

        string outputPath = Path.Combine(AssetBundleOutputPath, OutputBundleName);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        BuildPipeline.BuildAssetBundles(outputPath, assetBundleBuilds, CheckIncrementalMode(), BuildTarget.StandaloneWindows);
        Debug.Log($"AB包打包完成，储存路径为：{AssetBundleOutputPath}");

        //string buildVersionPath=Path.Combine(outputPath)
        string assetBundleVersionPath = Path.Combine(AssetBundleOutputPath, AssetManagerConfig.CurrentBuildVersion.ToString());

        if (!Directory.Exists(assetBundleVersionPath))
        {
            Directory.CreateDirectory(assetBundleVersionPath);
        }

        BuildAssetBundleHashTable(assetBundleBuilds, outputPath, assetBundleVersionPath);

        CopyAssetBundleToVersionFolder(outputPath, assetBundleVersionPath);


        BuildPackageTable(packageInfoDic, assetBundleVersionPath);

        AssetManagerConfig.CurrentBuildVersion++;

        AssetDatabase.Refresh();
    }
    static void NotKnow(AssetBundleBuild[] assetBundleBuilds)
    {
        // 放在这个BuildAssetBundleFromDirectedGraph()中的下面这句话后面，
        // 下面两句话是重复部分，直接将代码复制过去，并删除原来的后面的
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, assetBundleBuilds,
            CheckCompressionPattern(), BuildTarget.StandaloneWindows);
        Debug.Log($"AB包打包完成，储存路径为：{AssetBundleOutputPath}");

        // 得到这次打包的所有AssetBundle
        string[] assetBundleNames = manifest.GetAllAssetBundles();
        string[] assetBundleHashs = new string[assetBundleNames.Length];
        for (int i = 0; i < assetBundleNames.Length; i++)
        {
            string assetBundlePath = Path.Combine(AssetBundleOutputPath, assetBundleNames[i]);
            assetBundleHashs[i] = $"{ComputeAssetBundleSizeToMD5(assetBundlePath)}_{assetBundleNames[i]}";
        }

        StringBuilder assetBundleHashString = new StringBuilder();
        foreach (string hash in assetBundleHashs)
        {
            assetBundleHashString.Append($"{hash}\r\n");
        }

        // 写入文件
        string assetBundleHashsFilePath = Path.Combine(AssetBundleOutputPath, "AssetBundleHashFile.json");
        FileStream fileStream = File.Open(assetBundleHashsFilePath, FileMode.OpenOrCreate);
        byte[] bytes = Encoding.ASCII.GetBytes(assetBundleHashString.ToString());
        fileStream.Write(bytes, 0, bytes.Length);

        // 刷新、关闭、释放文件流对象
        fileStream.Flush();
        fileStream.Close();
        fileStream.Dispose();
        AssetDatabase.Refresh();
    }

    // 计算整个AssetBundle中所有Asset，将AssetGUID转换成byte[]
    static string ComputeAssetSetSignature(IEnumerable<string> assetNames)
    {
        var assetGuids = assetNames.Select(AssetDatabase.AssetPathToGUID);
        MD5 md5 = MD5.Create();

        // 如果传入的asset数量和路径都相同，那么可以得到相同的MD5哈希值
        foreach (string assetGuid in assetGuids.OrderBy(x => x))
        {
            byte[] buffer = Encoding.ASCII.GetBytes(assetGuid);
            md5.TransformBlock(buffer, 0, buffer.Length, null, 0);
        }

        md5.TransformFinalBlock(new byte[0], 0, 0);
        return BytesToHexString(md5.Hash);
    }

    // byte转16进制字符串
    static string BytesToHexString(byte[] bytes)
    {
        StringBuilder byteString = new StringBuilder();
        foreach (byte aByte in bytes)
        {
            byteString.Append(aByte.ToString("x2"));
        }
        return byteString.ToString();
    }

    static string ComputeAssetBundleSizeToMD5(string assetBundlePath)
    {
        MD5 md5 = MD5.Create();
        FileInfo fileInfo = new FileInfo(assetBundlePath);
        byte[] buffer = Encoding.ASCII.GetBytes(fileInfo.Length.ToString());
        md5.TransformBlock(buffer, 0, buffer.Length, null, 0);
        md5.TransformFinalBlock(new byte[0], 0, 0);

        return BytesToHexString(md5.Hash);
    }

    // 构建AB包的hash表，即记录AB包的大小和hash值
    static string[] BuildAssetBundleHashTable(AssetBundleBuild[] assetBundleBuilds, string outputPath, string versionPath)
    {
        string[] assetBundleHashs = new string[assetBundleBuilds.Length];
        for (int i = 0; i < assetBundleBuilds.Length; i++)
        {
            string assetBundlePath = Path.Combine(outputPath, assetBundleBuilds[i].assetBundleName);
            FileInfo fileInfo = new FileInfo(assetBundlePath);

            // 新的命名规则为：包体大小_包体内的AssetGUID的MD5码
            assetBundleHashs[i] = $"{fileInfo.Length}_{assetBundleBuilds[i].assetBundleName}";

        }

        // 写入文件
        string hashString = JsonConvert.SerializeObject(assetBundleHashs);
        string hashFilePath = Path.Combine(versionPath, "AssetBundleHashs");
        File.WriteAllText(hashFilePath, hashString);

        return assetBundleHashs;
    }

    // 之前的BuildHashTable方法中，将本次生成的所有AB包，代表了这次版本内容
    static AssetBundleVersionDifference ContrastAssetBundleVersion(string[] oldVersion, string[] newVersion)
    {
        AssetBundleVersionDifference difference = new AssetBundleVersionDifference();
        difference.AddtionAssetBundles = new List<string>();
        difference.ReducesAssetBundels = new List<string>();

        // 对比每一个老版本的ab包(hash表)，如果新版本不存在该包，则视为需要减少的包
        foreach (var assetBundle in oldVersion)
        {
            if (!newVersion.Contains(assetBundle))
            {
                difference.ReducesAssetBundels.Add(assetBundle);
            }
        }

        // 对比每一个新版本的ab包(hash表)，如果老版本不存在该包，则视为需要新增的包
        foreach (var assetBundle in newVersion)
        {
            if (!oldVersion.Contains(assetBundle))
            {
                difference.AddtionAssetBundles.Add(assetBundle);
            }
        }
        return difference;
    }

    static string[] ReadAssetBundleHashTable(string outpuePath)
    {
        string VersionHashTablePath = Path.Combine(outpuePath, "AssetBundleHashs");
        string VersionHashString = File.ReadAllText(VersionHashTablePath);
        string[] VersionAssetHashs = JsonConvert.DeserializeObject<string[]>(VersionHashString);
        return VersionAssetHashs;
    }
    static void CopyAssetBundleToVersionFolder(string oringinPath, string targetPath)
    {

        string[] assetNames = ReadAssetBundleHashTable(targetPath);

        // 复制哈希表
        //string hashTableOriginPath = Path.Combine(outputPath, "AssetBundleHashs");
        //string hashTableVersionPath = Path.Combine(assetBundleVersionOutputPath, "AssetBundleHashs");
        //File.Copy(hashTableOriginPath, hashTableVersionPath, true);

        // 复制主包
        string mainBundleOriginPath = Path.Combine(oringinPath, OutputBundleName);
        string mainBundleVersionPath = Path.Combine(targetPath, OutputBundleName);
        File.Copy(mainBundleOriginPath, mainBundleVersionPath, true);

        // 复制PackageInfos
        //string packageInfoPath = Path.Combine(outputPath, PackageTableName);
        //string packageInfoVersionPath = Path.Combine(assetBundleVersionOutputPath, PackageTableName);
        //File.Copy(packageInfoPath, packageInfoVersionPath, true);

        foreach (var assetName in assetNames)
        {
            string assetHashName = assetName.Substring(assetName.IndexOf("_") + 1);

            // file.Name是包含了拓展名的文件名
            string assetOriginPath = Path.Combine(oringinPath, assetHashName);
            // file.FullName是包含了目录和文件名的完整路径
            string assetVersionPath = Path.Combine(targetPath, assetHashName);

            File.Copy(assetOriginPath, assetVersionPath, true); // 加一个true，表示覆盖
        }
    }

    // 将ScriptableObject的内容保存为Json格式
    public static void SaveConfigToJSON()
    {
        if (AssetManagerConfig != null)
        {
            string configString = JsonUtility.ToJson(AssetManagerConfig);
            string outputPath = Path.Combine(Application.dataPath, "Editor/AssetManagerConfig.json");
            File.WriteAllText(outputPath, configString);
            AssetDatabase.Refresh();
        }
    }

    // 先加载Config文件
    public static void LoadAssetManagerConfig(AssetManagerEditorWindow window)
    {
        if (AssetManagerConfig == null)
        {
            // 使用AssetDataBase加载资源，只需要传入Assets目录下的路径即可
            AssetManagerConfig = AssetDatabase.LoadAssetAtPath<AssetManagerConfigScriptableObject>(
                                                                    "Assets/Editor/AssetManagerConfig.asset");
            window.VersionString = AssetManagerConfig.AssetManagerVersion.ToString();
            for (int i = window.VersionString.Length - 1; i >= 1; i--)
            {
                window.VersionString = window.VersionString.Insert(i, ".");
            }

        }
    }

    // 再从Json中读取Config
    public static void ReadConfigFromJSON()
    {
        string configPath = Path.Combine(Application.dataPath, "Editor/AssetManagerConfig.json");
        string configString = File.ReadAllText(configPath);
        JsonUtility.FromJsonOverwrite(configString, AssetManagerConfig);
    }

    public static void GetFolderAllAssets()
    {
        //if(AssetManagerConfig.AssetBundleDirectory == null)
        //{
        //    AssetManagerConfig.CurrentAllAssets.Clear();
        //    return;
        //}
        //string directoryPath = AssetDatabase.GetAssetPath(AssetManagerConfig.AssetBundleDirectory);
        //AssetManagerConfig.CurrentAllAssets = AssetManagerConfig.FindAllAssetPathFromDirectory(directoryPath);
        //AssetManagerConfig.CurrentSelectedAssets = new bool[AssetManagerConfig.CurrentAllAssets.Count];
    }

    // 加载WindowConfig文件
    public static void LoadAssetManagerWindowConfig(AssetManagerEditorWindow window)
    {
        if (window.WindowConfig == null)
        {
            // 使用AssetDataBase加载资源，只需要传入Assets目录下的路径即可
            window.WindowConfig = AssetDatabase.LoadAssetAtPath<AssetManagerEditorWindowConfig>(
                                                              "Assets/Editor/AssetManagerWindowConfig.asset");

            #region 标题LOGO
            window.WindowConfig.LogoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/1.jpg");
            window.WindowConfig.LogoTextureStyle = new GUIStyle();
            window.WindowConfig.LogoTextureStyle.fixedWidth = window.WindowConfig.LogoTexture.width / 2;
            window.WindowConfig.LogoTextureStyle.fixedHeight = window.WindowConfig.LogoTexture.height / 2;
            #endregion

            #region 标题
            window.WindowConfig.TitleTextStyle = new GUIStyle();
            window.WindowConfig.TitleTextStyle.fontSize = 24;
            window.WindowConfig.TitleTextStyle.normal.textColor = Color.red;
            window.WindowConfig.TitleTextStyle.alignment = TextAnchor.MiddleCenter;
            #endregion

            #region 版本号
            window.WindowConfig.VersionTextStyle = new GUIStyle();
            window.WindowConfig.VersionTextStyle.fontSize = 16;
            window.WindowConfig.VersionTextStyle.normal.textColor = Color.green;
            window.WindowConfig.VersionTextStyle.alignment = TextAnchor.MiddleCenter;
            #endregion
        }
    }

    public static void AddPackageInfoEditor()
    {
        AssetManagerConfig.packageInfoEditors.Add(new PackageEditorInfo());
    }

    public static void RemovePackageInfoEditors(PackageEditorInfo info)
    {
        if (AssetManagerConfig.packageInfoEditors.Contains(info))
        {
            AssetManagerConfig.packageInfoEditors.Remove(info);
        }
    }

    public static void AddAsset(PackageEditorInfo info)
    {
        info.AssetList.Add(null);
    }

    public static void RemoveAsset(PackageEditorInfo info, UnityEngine.Object asset)
    {
        if (info.AssetList.Contains(asset))
        {
            info.AssetList.Remove(asset);
        }
    }

    public static string PackageTableName = "AllPackages";
    static void BuildPackageTable(Dictionary<string, PackageBuildInfo> packages, string outputPath)
    {
        string packasgesPath = Path.Combine(outputPath, PackageTableName);

        // Package字典，key为包名
        string packagesJSON = JsonConvert.SerializeObject(packages.Keys);

        File.WriteAllText(packasgesPath, packagesJSON);

        foreach (PackageBuildInfo package in packages.Values)
        {
            packasgesPath = Path.Combine(outputPath, package.PackageName);
            packagesJSON = JsonConvert.SerializeObject(package);

            File.WriteAllText(packasgesPath, packagesJSON);
        }
    }
}

