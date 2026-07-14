
from ..structwrapper import BinReader
import numpy as np
import bpy
import math
import os
from mathutils import Vector

def read_cstring(f):
    buf = bytearray()
    while True:
        b = f.read(1)
        if not b or b == b"\x00":
            break
        buf += b
    return buf.decode("utf-8", errors="replace")

class MDLBatch:
    def __init__(self, f : BinReader):
        self.mesh_group_id = f.read_u32()
        self.material_id = f.read_u32()
        self.bone_map_id = f.read_u32() 
        self.vertex_group = f.read_u32() 
        self.indice_count = f.read_u32()
        self.indice_start = f.read_u32()
        self.vertex_count = f.read_u32()

class MDLLOD:
    def __init__(self, f : BinReader):
        self.u_0 = f.read_u32()
        self.name = f.read(4).decode()
        self.u_8 = f.read_u32()
        self.u_C = f.read_u32()
        self.u_10 = f.read_u32()
        self.u_14 = f.read_u32() # Not 0, 4
        self.u_18 = f.read_u32()
        self.u_1C = f.read_u32() # Not 0, 15
        self.u_20 = f.read_u32()
        self.u_24 = f.read_u32()
        self.batch_count = f.read_u32()

class MDLVertexHeader:
    def __init__(self, f : BinReader):
        self.main_size = f.read_u32()
        self.ex_size = f.read_u32()

class MDLBone:
    def __init__(self, f : BinReader):
        self.parent_id = f.read_u32()
        self.a = f.read_f32_vector3() # Maybe???
        self.b = f.read_f32_vector3()
        self.c = f.read_f32_vector3()
        self.translation = f.read_f32_vector3()
        self.rotation = f.read_f32_vector3()
        self.scale = f.read_f32_vector3()

class MDLBoneInfo:
    def __init__(self, f : BinReader):
        self.a = f.read_f32_vector3()
        self.b = f.read_f32_vector3()
        self.related_bone_id = f.read_u32()



FMT_INFO_ARRAY = {
    6   :   "f4",
    2   :   "f2",
    8   :   "u1",
    7   :   "u1",
    1   :   "f2",
}

FMT_SIZE_ARRAY = {
    6   :   4, # Position
    2   :   4, # Normal
    8   :   4, # Tangent/Color
    7   :   4, # BIX
    1   :   2,
}


def ImportMDL(filepath):
    rf = open(filepath, "rb")
    f = BinReader(rf)

    tag = f.read(4)
    if (tag != b"MDL\0"):
        print("[!] Not an MDL file!")


    if ("MDL" not in bpy.data.collections):
        wmb_collection = bpy.data.collections.new("MDL")
        bpy.context.scene.collection.children.link(wmb_collection)
    else:
        wmb_collection = bpy.data.collections["MDL"]

    wmb_name = os.path.splitext(os.path.basename(filepath))[0]
    model_collection = bpy.data.collections.new(wmb_name)
    wmb_collection.children.link(model_collection)
    

    size = f.read_u32()
    u_8 = f.read_u32()
    version = f.read_u32()
    bone_count = f.read_u32()
    u_14 = f.read_u32()
    bone_data_offset = f.read_u32()
    bone_name_table = f.read_u32()
    bone_info_count = f.read_u32()
    bone_info_offset = f.read_u32()
    lod_count = f.read_u32()
    lod_offset = f.read_u32() # LOD, probably
    lod_name_offset = f.read_u32()
    material_count = f.read_u32()
    material_names_offset = f.read_u32()
    mesh_name_count = f.read_u32()
    mesh_name_offset = f.read_u32()
    batch_count = f.read_u32()
    batch_offset = f.read_u32()
    vertex_property_count = f.read_u32()
    vertex_property_offset = f.read_u32()
    vertex_property_name_offset = f.read_u32()
    u_8 = f.read_u32()
    vertex_header_offset = f.read_u32()
    vertex_group_count = f.read_u32()
    vertex_group_sizes_offset = f.read_u32()
    vertex_pool_offsets_offset = f.read_u32()
    indice_count = f.read_u32()
    indice_offset = f.read_u32()
    remap_table_count = f.read_u32()
    remap_count_offset = f.read_u32()
    remap_table_offset = f.read_u32()


    bone_names = []
    bones = []
    for i in range(bone_count):
        f.seek(bone_name_table+(i*4))
        f.seek(f.read_u32())
        bone_names.append(read_cstring(f.f))

    f.seek(bone_data_offset)
    for _ in range(bone_count):
        bones.append(MDLBone(f))

    bone_infos_map = {}
    f.seek(bone_info_offset)
    for _ in range(bone_info_count):
        bi = MDLBoneInfo(f)
        bone_infos_map[bi.related_bone_id] = bi

    bpy_bones = []
    bone_remapping_array = []
    is_skinned = (bone_count > 0)
    if (is_skinned):
        arm_data = bpy.data.armatures.new(wmb_name)
        p_obj = bpy.data.objects.new(wmb_name, arm_data)
        



        model_collection.objects.link(p_obj)
        bpy.context.view_layer.objects.active = p_obj
        bpy.ops.object.mode_set(mode='EDIT')
        #p_obj.show_in_front = True

        parent_ids = {}
        
        for i, mdl_bone in enumerate(bones):
            parent_ids[i] = mdl_bone.parent_id
            world_pos = Vector((mdl_bone.translation[0], -mdl_bone.translation[2], mdl_bone.translation[1]))
            bone = arm_data.edit_bones.new(bone_names[i])
            bone.head = world_pos
            bone.tail = bone.head + Vector((0.0, 0.05, 0.0))

            if (i in bone_infos_map):
                # Custom properties
                bi = bone_infos_map[i]
                bone["pos_a"] = bi.a
                bone["pos_b"] = bi.b

            bone["bonedata_a"] = bones[i].a
            bone["bonedata_b"] = bones[i].b
            bone["bonedata_c"] = bones[i].c

            bone["bonedata_d"] = bones[i].translation
            bone["bonedata_e"] = bones[i].rotation
            bone["bonedata_f"] = bones[i].scale

            bpy_bones.append(bone)

        for i, bone in enumerate(bpy_bones):
            parent_id = parent_ids[i]
            if parent_id != i and parent_id < len(bpy_bones):
                bone.parent = bpy_bones[parent_id]

        children_map = {i: [] for i in range(len(bpy_bones))}
        for i, bone in enumerate(bpy_bones):
            parent_id = parent_ids[i]

            # root bone
            if i == 0:
                continue

            # valid parent
            if 0 <= parent_id < len(bpy_bones):
                bone.parent = bpy_bones[parent_id]



        for i, bone in enumerate(bpy_bones):
            children = children_map[i]
            if children:
                bone.tail = bpy_bones[children[0]].head
            elif bone.parent:
                direction = (bone.head - bone.parent.head).normalized()
                if direction.length > 0:
                    bone.tail = bone.head + direction * 0.05

        if (remap_table_count>1):
            print("[!] More than 1 remapping table, note this!")
        
        remap_counts = []
        f.seek(remap_count_offset)
        for _ in range(remap_table_count):
            remap_counts.append(f.read_u32())

        f.seek(remap_table_offset)
        for i in range(remap_table_count):
            array = []
            for _ in range(remap_counts[i]):
                array.append(f.read_u16())
            bone_remapping_array.append(array)

    f.seek(vertex_header_offset)
    vtx_header = MDLVertexHeader(f)

    f.seek(vertex_pool_offsets_offset)
    vtx_pool_offset = f.read_u32()
    exvtx_pool_offset = f.read_u32()



    f.seek(vertex_group_sizes_offset)
    vtx_group_sizes = []
    for _ in range(vertex_group_count):
        vtx_group_sizes.append(f.read_u32())


    vertex_fmt_names = []
    for i in range(vertex_property_count):
        f.seek(vertex_property_name_offset+(i*4))
        f.seek(f.read_u32())
        name = read_cstring(f.f)

        vertex_fmt_names.append(name)

    vertex_pool_attributes = []
    ex_pool_attributes = []
    uv_count = 0
    max_vertex_weight = 0

    f.seek(vertex_property_offset)
    for i in range(vertex_property_count):
        is_ex = f.read_u16()
        format = f.read_u16()
        slot_idx = f.read_u16()
        uv_map_idx = f.read_u16()
        if (uv_map_idx+1 > uv_count):
            uv_count = uv_map_idx+1

        byte_offset = f.read_u16()

        if (vertex_fmt_names[i] == 'BIX'):
            max_vertex_weight+=1
            vertex_fmt_names[i] = f"BIX_{max_vertex_weight}"
        elif (vertex_fmt_names[i] == 'BWT'):
            vertex_fmt_names[i] = f"BWT_{max_vertex_weight}"



        if (is_ex == 0):
            vertex_pool_attributes.append((vertex_fmt_names[i], FMT_INFO_ARRAY[format], FMT_SIZE_ARRAY[format]))
        else:
            ex_pool_attributes.append((vertex_fmt_names[i], FMT_INFO_ARRAY[format], FMT_SIZE_ARRAY[format]))

    print(vertex_pool_attributes)

    f.seek(indice_offset)
    indexes = f.read_u16_array(indice_count)


    f.seek(batch_offset)
    batches = []
    for _ in range(batch_count):
        batches.append(MDLBatch(f))

    f.seek(lod_offset)
    lods = []
    for _ in range(lod_count):
        lods.append(MDLLOD(f))

    mesh_names = []
    for i in range(mesh_name_count):
        f.seek(mesh_name_offset+(i*4))
        f.seek(f.read_u32())
        mesh_names.append(read_cstring(f.f))

    vertexes = []
    exvertexes = []
    f.seek(vtx_pool_offset)
    for i in range(vertex_group_count):
        dt = np.dtype(vertex_pool_attributes)
        vtx_data = np.fromfile(f.f, dt, vtx_group_sizes[i])
        vertexes.append(vtx_data)

    f.seek(exvtx_pool_offset)
    for i in range(vertex_group_count):
        dt = np.dtype(ex_pool_attributes)
        vtx_data = np.fromfile(f.f, dt, vtx_group_sizes[i])
        exvertexes.append(vtx_data)


    model_collection["version"] = version
    model_collection["lod_level"] = lods[0].name 
    model_collection["vtx_size"] = vtx_header.main_size
    model_collection["ex_size"] = vtx_header.ex_size
    model_collection["vertex_groups"]=vtx_group_sizes


    for lod in lods:
        for i in range(lod.batch_count):
            
            batch = batches[i]
            batch_indices = indexes[batch.indice_start:batch.indice_start + batch.indice_count]
            min_idx = np.min(batch_indices)
            max_idx = np.max(batch_indices)

            object_name = f"{i}-{mesh_names[batch.mesh_group_id]}"
            
            group_verts = vertexes[batch.vertex_group]
            group_verts = group_verts[min_idx:max_idx + 1]
            ex_group_verts = exvertexes[batch.vertex_group]
            ex_group_verts = ex_group_verts[min_idx:max_idx + 1]

            rebased_indices = batch_indices - min_idx
            faces = rebased_indices.reshape(-1, 3)


            mesh = bpy.data.meshes.new(object_name)
            obj = bpy.data.objects.new(object_name, mesh)
            obj.rotation_euler = (math.radians(90), 0, 0)
            model_collection.objects.link(obj)



            mesh.vertices.add(len(group_verts))

            pos = group_verts['POS'][:, :3]
            mesh.vertices.foreach_set('co', pos.astype(np.float32).ravel())

            mesh.loops.add(len(rebased_indices))
            mesh.polygons.add(len(faces))

            mesh.loops.foreach_set("vertex_index", rebased_indices)

            loop_start = np.arange(0, len(rebased_indices), 3)
            loop_total = np.full(len(faces), 3)

            mesh.polygons.foreach_set("loop_start", loop_start)
            mesh.polygons.foreach_set("loop_total", loop_total)

            mesh.update()
            norm_array = group_verts['NML'][:, :3].astype(np.float32)
            norm_array *= -1.0

            lengths = np.linalg.norm(norm_array, axis=1, keepdims=True)
            lengths[lengths == 0] = 1.0
            norm_array /= lengths

            mesh.normals_split_custom_set_from_vertices(norm_array.tolist())


            mesh.update()

            for uv_idx in range(uv_count):
                uv_name = f"map{uv_idx+1}"

                if uv_name in ex_group_verts.dtype.names:
                    uv_layer = mesh.uv_layers.new(name=uv_name)

                    uv_data = ex_group_verts[uv_name]

                    uv_coords = uv_data[:, :2].astype(np.float32)

                    uv_coords[:, 1] = 1.0 - uv_coords[:, 1]

                    loop_uvs = uv_coords[rebased_indices]

                    uv_layer.data.foreach_set(
                        "uv",
                        loop_uvs.ravel()
                    )

            if is_skinned:
                arm_mod = obj.modifiers.new(name="Armature", type='ARMATURE')
                arm_mod.object = p_obj

                obj.parent = p_obj

                for bone_name in bone_names:
                    if bone_name not in obj.vertex_groups:
                        obj.vertex_groups.new(name=bone_name)

                weight_layers = []
                for weight_idx in range(1, max_vertex_weight + 1):
                    bix_name = f"BIX_{weight_idx}"
                    bwt_name = f"BWT_{weight_idx}"

                    if bix_name in group_verts.dtype.names and bwt_name in group_verts.dtype.names:
                        weight_layers.append((
                            group_verts[bix_name],
                            group_verts[bwt_name]
                        ))

                for vert_idx in range(len(group_verts)):
                    influences = []

                    for bix_data, bwt_data in weight_layers:
                        for local_bone, raw_weight in zip(bix_data[vert_idx], bwt_data[vert_idx]):
                            if raw_weight == 0:
                                continue

                            global_bone = (
                                bone_remapping_array[batch.bone_map_id][local_bone]
                                if bone_remapping_array[batch.bone_map_id] else local_bone
                            )

                            influences.append((global_bone, raw_weight))

                    if not influences:
                        continue

                    weight_sum = sum(weight for _, weight in influences)
                    if weight_sum == 0:
                        continue

                    for bone_idx, raw_weight in influences:
                        bone_name = bone_names[bone_idx]

                        normalized_weight = raw_weight / weight_sum
                        obj.vertex_groups[bone_name].add(
                            [vert_idx],
                            normalized_weight,
                            'REPLACE'
                        )
    
    bpy.ops.object.mode_set(mode='OBJECT')
