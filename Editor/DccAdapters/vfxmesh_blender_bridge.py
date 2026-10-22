"""VFX Mesh Tool <-> Blender bridge.

Unity の VFX Mesh Tool (DCC Link) が Blender をこのスクリプト付きで起動する。

    blender --python vfxmesh_blender_bridge.py -- <name.manifest.json>            新しく受け取る
    blender <name.blend> --python vfxmesh_blender_bridge.py -- <manifest> --resume 続きから開く
    ... -- <manifest> --autosend                                                   受け取ってすぐ送り返して終了 (テスト用)

- 受け取り: Unity で今の設定を書き出したファイルを読む。
  OBJ (四角形) のときは、OBJ に入らないアルファを含む頂点カラーを colors.json から付け直す。
  FBX (三角形、RGBA 頂点カラー) のときは、cm 単位のスケールと回転を頂点に焼き込む。
  そのまま .blend として保存するので、Ctrl+S ですぐ保存できる。
- 送り返し: 3D ビューのサイドバー (N) > VFX Mesh タブの "Send to Unity"、または保存 (Ctrl+S) のたびに自動で。
  モディファイアとオブジェクトの移動・回転・拡大を焼き込んだメッシュを FBX にして、Unity の戻りフォルダに置く。
  Unity はそれを取り込んで、送ったときに選んでいたシーンのオブジェクトのメッシュを差し替える。
"""

import json
import os
import sys

import bpy
from bpy.app.handlers import persistent

PROP = "vfx_bridge_manifest"
SEND_TEMP_NAME = "__vfx_bridge_send"


def _script_args():
    argv = sys.argv
    return argv[argv.index("--") + 1:] if "--" in argv else []


def _load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


# ---------------------------------------------------------------- receive

def apply_colors(mesh, colors_path):
    """colors.json (OBJ の v 行と同じ順の RGBA) を頂点カラー属性として付ける。

    Unity の頂点カラーはそのままの値なので、sRGB の値として入れる (FBX を SRGB で書き出すと元の値に戻る)。
    """
    flat = _load(colors_path)["colors"]
    count = len(flat) // 4
    if count != len(mesh.vertices):
        print("VFX Mesh bridge: vertex count mismatch (%d colours, %d vertices), colours skipped" % (count, len(mesh.vertices)))
        return
    for attr in list(mesh.color_attributes):
        mesh.color_attributes.remove(attr)
    attr = mesh.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for i, d in enumerate(attr.data):
        r, g, b, a = flat[i * 4:i * 4 + 4]
        if hasattr(d, "color_srgb"):
            d.color_srgb = (r, g, b, a)
        else:
            d.color = (_srgb_to_linear(r), _srgb_to_linear(g), _srgb_to_linear(b), a)
    mesh.color_attributes.active_color = attr
    mesh.color_attributes.render_color_index = 0


def _clear_scene():
    """起動時のシーン (既定の Cube / Camera / Light など) を空にする。

    read_homefile で読み直すと、ウィンドウありの起動ではその後の演算子のコンテキストが失われるので、データを直接消す。
    """
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.cameras, bpy.data.lights, bpy.data.materials):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def _window_context():
    """ウィンドウありの起動で演算子を呼ぶためのコンテキスト (3D ビューのある最初のウィンドウ)。バックグラウンドでは空。"""
    wm = bpy.context.window_manager
    if bpy.app.background or wm is None or not wm.windows:
        return {}
    win = wm.windows[0]
    areas = list(win.screen.areas)
    area = next((a for a in areas if a.type == "VIEW_3D"), areas[0] if areas else None)
    ctx = {"window": win, "screen": win.screen}
    if area is not None:
        ctx["area"] = area
        region = next((r for r in area.regions if r.type == "WINDOW"), None)
        if region is not None:
            ctx["region"] = region
    return ctx


def receive(manifest_path):
    with bpy.context.temp_override(**_window_context()):
        _receive(manifest_path)


def _receive(manifest_path):
    m = _load(manifest_path)
    mesh_file = m.get("meshFile") or m["objFile"]
    fmt = (m.get("format") or "obj").lower()
    _clear_scene()
    if fmt == "fbx":
        # Unity FBX Exporter は cm 単位で書くので、読み込むとスケール 0.01 のオブジェクトになる。
        # 編集しやすいように回転・スケールを頂点に焼き込んで、Transform を素の状態にする
        bpy.ops.import_scene.fbx(filepath=mesh_file)
        meshes = [o for o in bpy.context.selected_objects if o.type == "MESH"]
        if meshes:
            for o in bpy.context.selected_objects:
                o.select_set(o in meshes)
            bpy.context.view_layer.objects.active = meshes[0]
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        # メッシュ以外 (空のルートなど) が来ていたら消す
        for o in list(bpy.context.scene.objects):
            if o.type != "MESH":
                bpy.data.objects.remove(o, do_unlink=True)
    else:
        bpy.ops.wm.obj_import(filepath=mesh_file)
        meshes = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError("メッシュがありません: " + mesh_file)
    obj = meshes[0]
    obj.name = m["name"]
    obj.data.name = m["name"]
    if fmt != "fbx" and m.get("colorsFile"):
        apply_colors(obj.data, m["colorsFile"])
    elif obj.data.color_attributes:
        obj.data.color_attributes.active_color = obj.data.color_attributes[0]
        obj.data.color_attributes.render_color_index = 0
    obj[PROP] = manifest_path
    bpy.context.scene[PROP] = manifest_path
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    os.makedirs(os.path.dirname(m["blendFile"]), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=m["blendFile"])


# ---------------------------------------------------------------- send

def _source_objects(scene):
    tagged = [o for o in scene.objects if o.type == "MESH" and o.get(PROP)]
    if tagged:
        return tagged
    return [o for o in bpy.context.selected_objects if o.type == "MESH"]


def send(context=None):
    """モディファイアと Transform を焼き込んだメッシュを FBX にして戻りフォルダへ置く。

    書き出しは送信フォルダの一時ファイルに書いてから置き換えるので、Unity が書きかけのファイルを読むことはない。
    """
    context = context or bpy.context
    scene = context.scene
    manifest_path = scene.get(PROP)
    if not manifest_path or not os.path.exists(manifest_path):
        raise RuntimeError("Unity から受け取ったシーンではありません (manifest がありません)")
    m = _load(manifest_path)

    sources = _source_objects(scene)
    if not sources:
        raise RuntimeError("送るメッシュがありません")

    view_layer = context.view_layer
    prev_active = view_layer.objects.active
    prev_selected = [o for o in scene.objects if o.select_get()]
    depsgraph = context.evaluated_depsgraph_get()

    temps = []
    try:
        for o in sources:
            evaluated = o.evaluated_get(depsgraph)
            me = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
            me.transform(o.matrix_world)
            t = bpy.data.objects.new(SEND_TEMP_NAME, me)
            scene.collection.objects.link(t)
            temps.append(t)

        for o in scene.objects:
            o.select_set(False)
        for t in temps:
            t.select_set(True)
        view_layer.objects.active = temps[0]
        if len(temps) > 1:
            with context.temp_override(active_object=temps[0], selected_editable_objects=temps, selected_objects=temps):
                bpy.ops.object.join()
            temps = [temps[0]]
        temps[0].data.name = m["name"]

        temp_file = os.path.join(os.path.dirname(m["blendFile"]), m["name"] + ".sending.fbx")
        bpy.ops.export_scene.fbx(
            filepath=temp_file,
            use_selection=True,
            object_types={"MESH"},
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z",
            axis_up="Y",
            # 軸の変換をオブジェクトの回転ではなく頂点に焼き込む。Unity 側はメッシュだけを差し替えるので、
            # 回転として持たせると 90 度倒れて見える
            bake_space_transform=True,
            use_mesh_modifiers=False,
            mesh_smooth_type="OFF",
            colors_type="SRGB",
            add_leaf_bones=False,
            bake_anim=False,
            use_custom_props=False,
        )
        os.makedirs(os.path.dirname(m["returnFile"]), exist_ok=True)
        os.replace(temp_file, m["returnFile"])
        print("VFX Mesh bridge: sent " + m["returnFile"])
        return m["returnFile"]
    finally:
        for t in temps:
            me = t.data
            bpy.data.objects.remove(t, do_unlink=True)
            if me is not None and me.users == 0:
                bpy.data.meshes.remove(me)
        for o in scene.objects:
            if o.name.startswith(SEND_TEMP_NAME):
                continue
            o.select_set(o in prev_selected)
        if prev_active is not None and prev_active.name in scene.objects:
            view_layer.objects.active = prev_active


# ---------------------------------------------------------------- UI

class VFXBRIDGE_OT_send(bpy.types.Operator):
    bl_idname = "vfx_bridge.send"
    bl_label = "Send to Unity"
    bl_description = "モディファイアと Transform を焼き込んだメッシュを Unity に送る"

    def execute(self, context):
        try:
            path = send(context)
        except Exception as ex:  # noqa: BLE001 - UI に出す
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        self.report({"INFO"}, "Sent: " + os.path.basename(path))
        return {"FINISHED"}


class VFXBRIDGE_PT_panel(bpy.types.Panel):
    bl_label = "Unity Link"
    bl_idname = "VFXBRIDGE_PT_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "VFX Mesh"

    def draw(self, context):
        layout = self.layout
        manifest_path = context.scene.get(PROP)
        if not manifest_path:
            layout.label(text="Unity から開いたシーンではありません")
            return
        try:
            m = _load(manifest_path)
            layout.label(text=m["name"], icon="MESH_DATA")
        except OSError:
            layout.label(text="manifest が見つかりません", icon="ERROR")
            return
        layout.operator(VFXBRIDGE_OT_send.bl_idname, icon="EXPORT")
        layout.prop(context.scene, "vfx_bridge_autosend")


@persistent
def _on_save(_):
    scene = bpy.context.scene
    if scene is None or not scene.get(PROP) or not getattr(scene, "vfx_bridge_autosend", False):
        return
    try:
        send()
    except Exception as ex:  # noqa: BLE001 - 保存は止めない
        print("VFX Mesh bridge: send on save failed: " + str(ex))


def register():
    bpy.types.Scene.vfx_bridge_autosend = bpy.props.BoolProperty(
        name="Send on Save", description="保存 (Ctrl+S) のたびに Unity に送る", default=True)
    bpy.utils.register_class(VFXBRIDGE_OT_send)
    bpy.utils.register_class(VFXBRIDGE_PT_panel)
    if _on_save not in bpy.app.handlers.save_post:
        bpy.app.handlers.save_post.append(_on_save)


def _start(args):
    manifest_path = args[0]
    try:
        if "--resume" in args:
            # 既存の .blend を開いた状態で起動している。manifest だけ結び付け直す
            bpy.context.scene[PROP] = manifest_path
        else:
            receive(manifest_path)
    finally:
        register()
    if "--autosend" in args:
        send()
        bpy.ops.wm.quit_blender()


def main():
    args = _script_args()
    if not args:
        register()
        return
    if bpy.app.background:
        _start(args)
        return

    # ウィンドウありの起動では、--python の時点ではまだ UI ができていない (演算子が context is incorrect で失敗する)。
    # ウィンドウができてから読み込む
    def deferred():
        wm = bpy.context.window_manager
        if wm is None or not wm.windows:
            return 0.1  # まだ。0.1 秒後にもう一度
        try:
            _start(args)
        except Exception as ex:  # noqa: BLE001 - 失敗は Blender のコンソールと画面に出す
            import traceback
            traceback.print_exc()
            _report_error(str(ex))
        return None

    bpy.app.timers.register(deferred, first_interval=0.1)


def _report_error(message):
    def draw(self, _context):
        self.layout.label(text=message)
    try:
        bpy.context.window_manager.popup_menu(draw, title="VFX Mesh bridge: import failed", icon="ERROR")
    except Exception:  # noqa: BLE001 - ポップアップが出せなくてもコンソールには出ている
        pass


main()
