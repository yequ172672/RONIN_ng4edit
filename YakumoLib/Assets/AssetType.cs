using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.Assets
{
    public enum AssetType
    {
        Unknown = 0,
        AssetTable,
        SoundBank,
        SequenceTrackData,
        Motion,
        Vibration,
        MaterialInstance,
        StaticMesh,
        Prefab,
        Texture,
        RDBTable,
        RDBDatabase,
        WorldAOCaptured,
        Scene,
        VFXGroupData,
        VAT,
        SkeletalMesh,
        WwiseProject,
        WindData,
        BlendMotion,
        DataSheet,
        VFXPrimitiveData,
        IK,
        CubeRenderTarget,
        Movie,
        StaticCollision,
        CutScene,
        AtlasTexture,
        FogData,
        MergeMesh,
        AnimationTexture,
        PostProcessData,
        UserWidget,
        Material,
        MotionList,
        CurveData,
        CharLightData,
        LocalizeText,
        VFXAttachData,
        PhysicalMaterial,
        Font,
        AtmosphereData,
        Dynajoint,
        WeatherParamData,
        Imposter,
        MaterialLayerInstance,
        Destruction,
        FontStyle,
        CloudData,
        LocalizeFont,
        AppSample,
        IconFont,
        VFXATTRData,
        BlendShapeMesh,
        MaterialLayer,
        DayNightData,
        RainFallData,
        SeaAssetData,
        TextTagTable,
        Texture3D,
        NavMeshData
    }

    public static class AssetTypeParser
    {
        public static AssetType GetType(string name)
        {
            if (name == "AssetTable")
            {
                return AssetType.AssetTable;
            }
            else if (name == "SoundBank")
            {
                return AssetType.SoundBank;
            }
            else if (name == "SequenceTrackData")
            {
                return AssetType.SequenceTrackData;
            }
            else if (name == "Motion")
            {
                return AssetType.Motion;
            }
            else if (name == "Vibration")
            {
                return AssetType.Vibration;
            }
            else if (name == "Material")
            {
                return AssetType.Material;
            }
            else if (name == "MaterialInstance")
            {
                return AssetType.MaterialInstance;
            }
            else if (name == "StaticMesh")
            {
                return AssetType.StaticMesh;
            }
            else if (name == "Prefab")
            {
                return AssetType.Prefab;
            }
            else if (name == "Texture")
            {
                return AssetType.Texture;
            }
            else if (name == "RDBTable")
            {
                return AssetType.RDBTable;
            }
            else if (name == "WorldAoCaptured")
            {
                return AssetType.WorldAOCaptured;
            }
            else if (name == "Scene")
            {
                return AssetType.Scene;
            }
            else if (name == "VFXGroupData")
            {
                return AssetType.VFXGroupData;
            }
            else if (name == "VAT")
            {
                return AssetType.VAT;
            }
            else if (name == "SkeletalMesh")
            {
                return AssetType.SkeletalMesh;
            }
            else if (name == "WwiseProject")
            {
                return AssetType.WwiseProject;
            }
            else if (name == "WindData")
            {
                return AssetType.WindData;
            }
            else if (name == "BlendMotion")
            {
                return AssetType.BlendMotion;
            }
            else if (name == "DataSheet")
            {
                return AssetType.DataSheet;
            }
            else if (name == "VFXPrimitiveData")
            {
                return AssetType.VFXPrimitiveData;
            }
            else if (name == "IK")
            {
                return AssetType.IK;
            }
            else if (name == "CubeRenderTarget")
            {
                return AssetType.CubeRenderTarget;
            }
            else if (name == "Movie")
            {
                return AssetType.Movie;
            }
            else if (name == "StaticCollision")
            {
                return AssetType.StaticCollision;
            }
            else if (name == "CutScene")
            {
                return AssetType.StaticCollision;
            }
            else if (name == "AtlasTexture")
            {
                return AssetType.AtlasTexture;
            }
            else if (name == "FogData")
            {
                return AssetType.FogData;
            }
            else if (name == "MergeMesh")
            {
                return AssetType.MergeMesh;
            }
            else if (name == "AnimationTexture")
            {
                return AssetType.AnimationTexture;
            }
            else if (name == "PostProcessData")
            {
                return AssetType.PostProcessData;
            }
            else if (name == "UserWidget")
            {
                return AssetType.UserWidget;
            }
            else if (name == "MotionList")
            {
                return AssetType.MotionList;
            }
            else if (name == "CurveData")
            {
                return AssetType.CurveData;
            }
            else if (name == "CharLightData")
            {
                return AssetType.CharLightData;
            }
            else if (name == "LocalizeText")
            {
                return AssetType.LocalizeText;
            }
            else if (name == "VFXAttachData")
            {
                return AssetType.VFXAttachData;
            }
            else if (name == "PhysicalMaterial")
            {
                return AssetType.PhysicalMaterial;
            }
            else if (name == "Font")
            {
                return AssetType.Font;
            }
            else if (name == "AtmosphereData")
            {
                return AssetType.AtmosphereData;
            }
            else if (name == "Dynajoint")
            {
                return AssetType.AtmosphereData;
            }
            else if (name == "WeatherParamData")
            {
                return AssetType.WeatherParamData;
            }
            else if (name == "Imposter")
            {
                return AssetType.Imposter;
            }
            else if (name == "MaterialLayerInstance")
            {
                return AssetType.MaterialLayerInstance;
            }
            else if (name == "Destruction")
            {
                return AssetType.Destruction;
            }
            else if (name == "FontStyle")
            {
                return AssetType.FontStyle;
            }
            else if (name == "CloudData")
            {
                return AssetType.CloudData;
            }
            else if (name == "LocalizeFont")
            {
                return AssetType.LocalizeFont;
            }
            else if (name == "AppSample")
            {
                return AssetType.AppSample;
            }
            else if (name == "IconFont")
            {
                return AssetType.IconFont;
            }
            else if (name == "VFXATTRData")
            {
                return AssetType.VFXATTRData;
            }
            else if (name == "BlendShapeMesh")
            {
                return AssetType.BlendShapeMesh;
            }
            else if (name == "MaterialLayer")
            {
                return AssetType.MaterialLayer;
            }
            else if (name == "DayNightData")
            {
                return AssetType.DayNightData;
            }
            else if (name == "RainFallData")
            {
                return AssetType.RainFallData;
            }
            else if (name == "SeaAssetData")
            {
                return AssetType.RainFallData;
            }
            else if (name == "TextTagTable")
            {
                return AssetType.TextTagTable;
            }
            else if (name == "Texture3D")
            {
                return AssetType.Texture3D;
            }
            else if (name == "NavMeshData")
            {
                return AssetType.NavMeshData;
            }
            else if (name == "RDBDatabase")
            {
                return AssetType.RDBDatabase;
            }

            else
            {
                return AssetType.Unknown;
            }


        }
    }
    


}
