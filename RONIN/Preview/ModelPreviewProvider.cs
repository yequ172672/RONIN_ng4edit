using HelixToolkit;
using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using RONIN.Formats;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using YakumoLib.Assets;

namespace RONIN.Preview
{
    public sealed class ModelPreviewProvider : IAssetPreviewProvider 
    {
        public bool CanPreview(AssetEntry entry) => (entry.Type == AssetType.SkeletalMesh || entry.Type == AssetType.StaticMesh); // TODO: Statci mesh

        public async Task<object?> LoadPreviewAsync(AssetEntry entry)
        {
            if (entry.SubEntries is null || entry.SubEntries.Count == 0)
                return null;

            var modelDataHeader = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("modeldata.mdl"));

            if (modelDataHeader is null)
                return null;



            MDLHelixBuffers[] buffers = MDLParser.GetPreviewBuffers(AssetExtractor.GetSubBlob(modelDataHeader, entry, modelDataHeader.ContentDirectory));


            uint u = 0;


            MeshGeometryModel3D[] models = new MeshGeometryModel3D[buffers.Count()];

            foreach (MDLHelixBuffers buf in buffers)
            {
                models[u] = ToModel(buf);
                u++;
            }

            ModelPreviewViewModel vm = new ModelPreviewViewModel
            {
                Models = new ObservableCollection<MeshGeometryModel3D>(models)
            };

            return (object?)vm;

        }

        public static MeshGeometryModel3D ToModel(MDLHelixBuffers buf)
        {
            var mesh = new HelixToolkit.SharpDX.MeshGeometry3D
            {
                Positions = new Vector3Collection(buf.Positions),
                Normals = new Vector3Collection(buf.Normals),
                TextureCoordinates = new Vector2Collection(buf.UVs),
                Indices = new IntCollection(buf.Indices.Select(i => (int)i))
            };


            return new MeshGeometryModel3D
            {
                Geometry = mesh,
                Material = PhongMaterials.White 
            };
        }

    }
    public sealed class ModelPreviewViewModel()
    {
        public IEffectsManager EffectsManager { get; } = new DefaultEffectsManager();
        public HelixToolkit.Wpf.SharpDX.Camera Camera { get; } = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
            Position = new Point3D(3, 3, 5),
            LookDirection = new Vector3D(-3, -3, -5),
            UpDirection = new Vector3D(0, 1, 0),
            FarPlaneDistance = 10000,
            NearPlaneDistance = 0.1
        };
        public required ObservableCollection<MeshGeometryModel3D> Models { get; init; }
    }
}
