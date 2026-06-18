using CombatOverhaul.Armor;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace QuiversAndSheaths;

internal sealed class QsOverlayGizmoRenderer : IRenderer, IDisposable
{
    private const double AxisLength = 0.72;
    private const double PickDistance = 12;
    private const double CubeHalfSize = 0.055;
    private const double ArrowHeadLength = 0.14;
    private const double ArrowHeadWidth = 0.07;
    private const double MoveTransformUnitsPerPixel = 0.02;
    private const double ScaleUnitsPerPixel = 0.004;

    private static readonly int Red = ColorUtil.ColorFromRgba(255, 35, 35, 255);
    private static readonly int Green = ColorUtil.ColorFromRgba(35, 220, 35, 255);
    private static readonly int Blue = ColorUtil.ColorFromRgba(45, 120, 255, 255);
    private static readonly int Yellow = ColorUtil.ColorFromRgba(255, 230, 40, 255);
    private static readonly int White = ColorUtil.ColorFromRgba(255, 255, 255, 210);

    private readonly ICoreClientAPI _api;

    private QsOverlayGizmoAxis _hoveredAxis = QsOverlayGizmoAxis.None;
    private QsOverlayGizmoAxis _draggedAxis = QsOverlayGizmoAxis.None;
    private int _dragStartX;
    private int _dragStartY;
    private Vec2d _dragScreenAxis = new(1, 0);
    private Vec3d _dragWorldAxis = new(1, 0, 0);
    private Vec3d _dragRotateStartVector = new(1, 0, 0);
    private bool _dragHasRotateVector;
    private GizmoState _dragStartState;
    private Vec3f _dragStartTranslation = new();
    private Vec3f _dragStartRotation = new();
    private float _dragStartScale = 1f;
    private bool _disposed;

    public QsOverlayGizmoRenderer(ICoreClientAPI api)
    {
        _api = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "quiversandsheaths-qsoverlay-gizmo");
        api.Event.MouseDown += OnMouseDown;
        api.Event.MouseMove += OnMouseMove;
        api.Event.MouseUp += OnMouseUp;
    }

    public double RenderOrder => 0.94;
    public int RenderRange => 9999;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_disposed || stage != EnumRenderStage.Opaque || BackSlingTransformTuningCommands.GizmoMode == QsOverlayGizmoMode.Off)
        {
            return;
        }

        if (!TryBuildState(out GizmoState state))
        {
            _hoveredAxis = QsOverlayGizmoAxis.None;
            _draggedAxis = QsOverlayGizmoAxis.None;
            return;
        }

        if (_draggedAxis == QsOverlayGizmoAxis.None)
        {
            _hoveredAxis = PickAxis(state, _api.Input.MouseX, _api.Input.MouseY);
        }

        _api.Render.GLDisableDepthTest();
        DrawWireCube(state.Center, 0.035, White);
        DrawActiveGizmo(state);
        _api.Render.GLEnableDepthTest();
    }

    private void OnMouseDown(MouseEvent args)
    {
        if (args.Handled || args.Button != EnumMouseButton.Left || BackSlingTransformTuningCommands.GizmoMode == QsOverlayGizmoMode.Off)
        {
            return;
        }

        if (!TryBuildState(out GizmoState state)) return;

        QsOverlayGizmoAxis picked = PickAxis(state, args.X, args.Y);
        if (picked == QsOverlayGizmoAxis.None) return;
        if (!BackSlingTransformTuningCommands.TryResolveTarget(_api, out QsOverlayTarget target)) return;

        _draggedAxis = picked;
        _hoveredAxis = picked;
        _dragStartState = state;
        _dragStartX = args.X;
        _dragStartY = args.Y;
        _dragScreenAxis = GetScreenAxis(state, picked);
        _dragWorldAxis = GetWorldAxis(state, picked);
        _dragHasRotateVector = TryGetRotateVector(state.Center, _dragWorldAxis, args.X, args.Y, out _dragRotateStartVector);
        _dragStartTranslation = new Vec3f(target.Transform.Translation.X, target.Transform.Translation.Y, target.Transform.Translation.Z);
        _dragStartRotation = new Vec3f(target.Transform.Rotation.X, target.Transform.Rotation.Y, target.Transform.Rotation.Z);
        _dragStartScale = target.Transform.ScaleXYZ.X;
        args.Handled = true;
    }

    private void OnMouseMove(MouseEvent args)
    {
        if (BackSlingTransformTuningCommands.GizmoMode == QsOverlayGizmoMode.Off)
        {
            _hoveredAxis = QsOverlayGizmoAxis.None;
            _draggedAxis = QsOverlayGizmoAxis.None;
            return;
        }

        if (_draggedAxis == QsOverlayGizmoAxis.None)
        {
            _hoveredAxis = TryBuildState(out GizmoState state) ? PickAxis(state, args.X, args.Y) : QsOverlayGizmoAxis.None;
            return;
        }

        ApplyDrag(args.X, args.Y);
        args.Handled = true;
    }

    private void OnMouseUp(MouseEvent args)
    {
        if (_draggedAxis == QsOverlayGizmoAxis.None) return;

        _draggedAxis = QsOverlayGizmoAxis.None;
        _hoveredAxis = QsOverlayGizmoAxis.None;
        args.Handled = true;
    }

    private void ApplyDrag(int mouseX, int mouseY)
    {
        if (!BackSlingTransformTuningCommands.TryResolveTarget(_api, out QsOverlayTarget target)) return;

        double dx = mouseX - _dragStartX;
        double dy = mouseY - _dragStartY;
        double fallbackPixels = dx * _dragScreenAxis.X + dy * _dragScreenAxis.Y;

        switch (BackSlingTransformTuningCommands.GizmoMode)
        {
            case QsOverlayGizmoMode.Move:
                ApplyMoveDrag(target, fallbackPixels * MoveTransformUnitsPerPixel);
                break;
            case QsOverlayGizmoMode.Rotate:
                ApplyRotateDrag(target, mouseX, mouseY, fallbackPixels);
                break;
            case QsOverlayGizmoMode.Scale:
                ApplyScaleDrag(target, fallbackPixels * ScaleUnitsPerPixel);
                break;
        }
    }

    private void ApplyMoveDrag(QsOverlayTarget target, double axisTransformDelta)
    {
        switch (_draggedAxis)
        {
            case QsOverlayGizmoAxis.X:
                target.Transform.Translation.X = _dragStartTranslation.X + (float)axisTransformDelta;
                break;
            case QsOverlayGizmoAxis.Y:
                target.Transform.Translation.Y = _dragStartTranslation.Y + (float)axisTransformDelta;
                break;
            case QsOverlayGizmoAxis.Z:
                target.Transform.Translation.Z = _dragStartTranslation.Z + (float)axisTransformDelta;
                break;
        }
    }

    private void ApplyScaleDrag(QsOverlayTarget target, double axisScaleDelta)
    {
        float scale = Math.Clamp(_dragStartScale + (float)(axisScaleDelta * 0.75), 0.05f, 5f);
        target.Transform.Scale = scale;
    }

    private void ApplyRotateDrag(QsOverlayTarget target, int mouseX, int mouseY, double fallbackPixels)
    {
        double deltaDegrees = fallbackPixels * 0.5;

        if (_dragHasRotateVector && TryGetRotateVector(_dragStartState.Center, _dragWorldAxis, mouseX, mouseY, out Vec3d currentVector))
        {
            double dot = Math.Clamp(Dot(_dragRotateStartVector, currentVector), -1, 1);
            double signed = Dot(_dragWorldAxis, _dragRotateStartVector.Cross(currentVector));
            deltaDegrees = Math.Atan2(signed, dot) * GameMath.RAD2DEG;
        }

        Vec3f rotation = RotateEulerComponent(_dragStartRotation, _draggedAxis, deltaDegrees);
        target.Transform.Rotation.X = rotation.X;
        target.Transform.Rotation.Y = rotation.Y;
        target.Transform.Rotation.Z = rotation.Z;
    }

    private void DrawActiveGizmo(GizmoState state)
    {
        switch (BackSlingTransformTuningCommands.GizmoMode)
        {
            case QsOverlayGizmoMode.Move:
                DrawAxisArrow(state, QsOverlayGizmoAxis.X);
                DrawAxisArrow(state, QsOverlayGizmoAxis.Y);
                DrawAxisArrow(state, QsOverlayGizmoAxis.Z);
                break;
            case QsOverlayGizmoMode.Rotate:
                DrawAxisCircle(state, QsOverlayGizmoAxis.X);
                DrawAxisCircle(state, QsOverlayGizmoAxis.Y);
                DrawAxisCircle(state, QsOverlayGizmoAxis.Z);
                break;
            case QsOverlayGizmoMode.Scale:
                DrawAxisCube(state, QsOverlayGizmoAxis.X);
                DrawAxisCube(state, QsOverlayGizmoAxis.Y);
                DrawAxisCube(state, QsOverlayGizmoAxis.Z);
                break;
        }
    }

    private void DrawAxisArrow(GizmoState state, QsOverlayGizmoAxis axis)
    {
        Vec3d direction = GetWorldAxis(state, axis);
        Vec3d end = Add(state.Center, Scale(direction, AxisLength));
        int color = AxisColor(axis);

        DrawLine(state.Center, end, color);
        Vec3d side = Perpendicular(direction, state.CameraUp);
        Vec3d side2 = Perpendicular(direction, state.CameraRight);
        Vec3d basePoint = Add(end, Scale(direction, -ArrowHeadLength));
        DrawLine(end, Add(basePoint, Scale(side, ArrowHeadWidth)), color);
        DrawLine(end, Add(basePoint, Scale(side, -ArrowHeadWidth)), color);
        DrawLine(end, Add(basePoint, Scale(side2, ArrowHeadWidth)), color);
        DrawLine(end, Add(basePoint, Scale(side2, -ArrowHeadWidth)), color);
    }

    private void DrawAxisCube(GizmoState state, QsOverlayGizmoAxis axis)
    {
        Vec3d direction = GetWorldAxis(state, axis);
        Vec3d end = Add(state.Center, Scale(direction, AxisLength));
        int color = AxisColor(axis);

        DrawLine(state.Center, end, color);
        DrawWireCube(end, CubeHalfSize, color);
    }

    private void DrawAxisCircle(GizmoState state, QsOverlayGizmoAxis axis)
    {
        GetCircleBasis(state, axis, out Vec3d u, out Vec3d v);
        int color = AxisColor(axis);
        Vec3d previous = Add(state.Center, Scale(u, AxisLength));

        for (int i = 1; i <= 64; i++)
        {
            double angle = GameMath.TWOPI * i / 64;
            Vec3d point = Add(state.Center, Add(Scale(u, Math.Cos(angle) * AxisLength), Scale(v, Math.Sin(angle) * AxisLength)));
            DrawLine(previous, point, color);
            previous = point;
        }
    }

    private void GetCircleBasis(GizmoState state, QsOverlayGizmoAxis axis, out Vec3d u, out Vec3d v)
    {
        Vec3d normal = GetWorldAxis(state, axis);
        u = Perpendicular(normal, state.CameraUp);
        v = normal.Cross(u).Normalize();
    }

    private int AxisColor(QsOverlayGizmoAxis axis)
    {
        if (axis == _draggedAxis || axis == _hoveredAxis) return Yellow;

        return axis switch
        {
            QsOverlayGizmoAxis.X => Red,
            QsOverlayGizmoAxis.Y => Green,
            QsOverlayGizmoAxis.Z => Blue,
            _ => Yellow
        };
    }

    private void DrawWireCube(Vec3d center, double halfSize, int color)
    {
        Vec3d x = Scale(new Vec3d(1, 0, 0), halfSize);
        Vec3d y = Scale(new Vec3d(0, 1, 0), halfSize);
        Vec3d z = Scale(new Vec3d(0, 0, 1), halfSize);

        Vec3d[] points =
        [
            Add(center, Add(Add(Scale(x, -1), Scale(y, -1)), Scale(z, -1))),
            Add(center, Add(Add(Scale(x, 1), Scale(y, -1)), Scale(z, -1))),
            Add(center, Add(Add(Scale(x, 1), Scale(y, 1)), Scale(z, -1))),
            Add(center, Add(Add(Scale(x, -1), Scale(y, 1)), Scale(z, -1))),
            Add(center, Add(Add(Scale(x, -1), Scale(y, -1)), Scale(z, 1))),
            Add(center, Add(Add(Scale(x, 1), Scale(y, -1)), Scale(z, 1))),
            Add(center, Add(Add(Scale(x, 1), Scale(y, 1)), Scale(z, 1))),
            Add(center, Add(Add(Scale(x, -1), Scale(y, 1)), Scale(z, 1)))
        ];

        DrawLine(points[0], points[1], color);
        DrawLine(points[1], points[2], color);
        DrawLine(points[2], points[3], color);
        DrawLine(points[3], points[0], color);
        DrawLine(points[4], points[5], color);
        DrawLine(points[5], points[6], color);
        DrawLine(points[6], points[7], color);
        DrawLine(points[7], points[4], color);
        DrawLine(points[0], points[4], color);
        DrawLine(points[1], points[5], color);
        DrawLine(points[2], points[6], color);
        DrawLine(points[3], points[7], color);
    }

    private void DrawLine(Vec3d start, Vec3d end, int color)
    {
        BlockPos origin = new((int)Math.Floor(start.X), (int)Math.Floor(start.Y), (int)Math.Floor(start.Z));
        _api.Render.RenderLine(origin, (float)(start.X - origin.X), (float)(start.Y - origin.Y), (float)(start.Z - origin.Z), (float)(end.X - origin.X), (float)(end.Y - origin.Y), (float)(end.Z - origin.Z), color);
    }

    private QsOverlayGizmoAxis PickAxis(GizmoState state, int mouseX, int mouseY)
    {
        return BackSlingTransformTuningCommands.GizmoMode == QsOverlayGizmoMode.Rotate
            ? PickCircleAxis(state, mouseX, mouseY)
            : PickLinearAxis(state, mouseX, mouseY);
    }

    private QsOverlayGizmoAxis PickLinearAxis(GizmoState state, int mouseX, int mouseY)
    {
        double best = PickDistance;
        QsOverlayGizmoAxis picked = QsOverlayGizmoAxis.None;

        foreach (QsOverlayGizmoAxis axis in new[] { QsOverlayGizmoAxis.X, QsOverlayGizmoAxis.Y, QsOverlayGizmoAxis.Z })
        {
            Vec3d end = Add(state.Center, Scale(GetWorldAxis(state, axis), AxisLength));
            if (!Project(state.Center, out Vec2d a) || !Project(end, out Vec2d b)) continue;

            double distance = DistancePointToSegment(mouseX, mouseY, a, b);
            if (distance >= best) continue;

            best = distance;
            picked = axis;
        }

        return picked;
    }

    private QsOverlayGizmoAxis PickCircleAxis(GizmoState state, int mouseX, int mouseY)
    {
        double best = PickDistance;
        QsOverlayGizmoAxis picked = QsOverlayGizmoAxis.None;

        foreach (QsOverlayGizmoAxis axis in new[] { QsOverlayGizmoAxis.X, QsOverlayGizmoAxis.Y, QsOverlayGizmoAxis.Z })
        {
            GetCircleBasis(state, axis, out Vec3d u, out Vec3d v);
            Vec3d first = Add(state.Center, Scale(u, AxisLength));
            if (!Project(first, out Vec2d previous)) continue;

            for (int i = 1; i <= 64; i++)
            {
                double angle = GameMath.TWOPI * i / 64;
                Vec3d point = Add(state.Center, Add(Scale(u, Math.Cos(angle) * AxisLength), Scale(v, Math.Sin(angle) * AxisLength)));
                if (!Project(point, out Vec2d projected)) continue;

                double distance = DistancePointToSegment(mouseX, mouseY, previous, projected);
                if (distance < best)
                {
                    best = distance;
                    picked = axis;
                }

                previous = projected;
            }
        }

        return picked;
    }

    private Vec2d GetScreenAxis(GizmoState state, QsOverlayGizmoAxis axis)
    {
        Vec3d end = Add(state.Center, Scale(GetWorldAxis(state, axis), AxisLength));
        if (!Project(state.Center, out Vec2d a) || !Project(end, out Vec2d b)) return new Vec2d(1, 0);

        Vec2d vector = new(b.X - a.X, b.Y - a.Y);
        double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        if (length < 0.001) return new Vec2d(1, 0);

        vector.X /= length;
        vector.Y /= length;
        return vector;
    }

    private bool Project(Vec3d worldPos, out Vec2d screenPos)
    {
        screenPos = new Vec2d();
        Vec3d projected = MatrixToolsd.Project(worldPos, _api.Render.PerspectiveProjectionMat, _api.Render.PerspectiveViewMat, _api.Render.FrameWidth, _api.Render.FrameHeight);
        if (projected.Z < 0) return false;

        screenPos.X = projected.X;
        screenPos.Y = _api.Render.FrameHeight - projected.Y;
        return true;
    }

    private bool TryGetMouseRay(int mouseX, int mouseY, out Vec3d origin, out Vec3d direction)
    {
        origin = new Vec3d();
        direction = new Vec3d();

        double[] projectionView = Mat4d.Create();
        Mat4d.Mul(projectionView, _api.Render.PerspectiveProjectionMat, _api.Render.PerspectiveViewMat);

        double[] inverse = Mat4d.Create();
        if (Mat4d.Invert(inverse, projectionView) == null) return false;
        if (!Unproject(inverse, mouseX, mouseY, -1, out Vec3d near)) return false;
        if (!Unproject(inverse, mouseX, mouseY, 1, out Vec3d far)) return false;

        direction = Sub(far, near);
        if (direction.LengthSq() < 0.000001) return false;

        origin = near;
        direction.Normalize();
        return true;
    }

    private bool Unproject(double[] inverseProjectionView, int mouseX, int mouseY, double clipZ, out Vec3d world)
    {
        world = new Vec3d();
        double ndcX = 2.0 * mouseX / _api.Render.FrameWidth - 1;
        double ndcY = 1 - 2.0 * mouseY / _api.Render.FrameHeight;
        double[] result = Mat4d.MulWithVec4(inverseProjectionView, new[] { ndcX, ndcY, clipZ, 1.0 });
        if (Math.Abs(result[3]) < 0.000001) return false;

        world.X = result[0] / result[3];
        world.Y = result[1] / result[3];
        world.Z = result[2] / result[3];
        return true;
    }

    private bool TryGetRotateVector(Vec3d center, Vec3d normal, int mouseX, int mouseY, out Vec3d vector)
    {
        vector = new Vec3d();
        if (!TryIntersectMousePlane(center, normal, mouseX, mouseY, out Vec3d point)) return false;

        vector = Sub(point, center);
        vector = Sub(vector, Scale(normal, Dot(vector, normal)));
        if (vector.LengthSq() < 0.00001) return false;

        vector.Normalize();
        return true;
    }

    private bool TryIntersectMousePlane(Vec3d planePoint, Vec3d planeNormal, int mouseX, int mouseY, out Vec3d point)
    {
        point = new Vec3d();
        if (!TryGetMouseRay(mouseX, mouseY, out Vec3d rayOrigin, out Vec3d rayDirection)) return false;

        double denom = Dot(planeNormal, rayDirection);
        if (Math.Abs(denom) < 0.00001) return false;

        double distance = Dot(planeNormal, Sub(planePoint, rayOrigin)) / denom;
        if (distance < 0) return false;

        point = Add(rayOrigin, Scale(rayDirection, distance));
        return true;
    }

    private bool TryBuildState(out GizmoState state)
    {
        state = default;
        if (!BackSlingTransformTuningCommands.TryResolveTarget(_api, out QsOverlayTarget target)) return false;
        if (_api.World.Player?.Entity is not EntityPlayer player) return false;
        if (!TryGetCenterAndAxes(player, target, target.Transform, out Vec3d center, out Vec3d axisX, out Vec3d axisY, out Vec3d axisZ)) return false;

        GetCameraBasis(out _, out Vec3d cameraRight, out Vec3d cameraUp);

        state = new GizmoState
        {
            Center = center,
            AxisX = axisX,
            AxisY = axisY,
            AxisZ = axisZ,
            CameraRight = cameraRight,
            CameraUp = cameraUp
        };

        return true;
    }

    private bool TryGetCenterAndAxes(EntityPlayer player, QsOverlayTarget target, ModelTransform transform, out Vec3d center, out Vec3d axisX, out Vec3d axisY, out Vec3d axisZ)
    {
        center = new Vec3d();
        axisX = new Vec3d(1, 0, 0);
        axisY = new Vec3d(0, 1, 0);
        axisZ = new Vec3d(0, 0, 1);

        if (!TryBuildGizmoMatrices(player, target, transform, out Matrixf centerMatrix, out Matrixf axisMatrix)) return false;

        Vec3d camera = player.CameraPos;
        center = TransformPoint(centerMatrix, camera, 0, 0, 0);

        Vec3d axisOrigin = TransformPoint(axisMatrix, camera, 0, 0, 0);
        axisX = NormalizeOrDefault(Sub(TransformPoint(axisMatrix, camera, 1, 0, 0), axisOrigin), new Vec3d(1, 0, 0));
        axisY = NormalizeOrDefault(Sub(TransformPoint(axisMatrix, camera, 0, 1, 0), axisOrigin), new Vec3d(0, 1, 0));
        axisZ = NormalizeOrDefault(Sub(TransformPoint(axisMatrix, camera, 0, 0, 1), axisOrigin), new Vec3d(0, 0, 1));
        return true;
    }

    private bool TryBuildGizmoMatrices(EntityPlayer player, QsOverlayTarget target, ModelTransform transform, out Matrixf centerMatrix, out Matrixf axisMatrix)
    {
        centerMatrix = new Matrixf();
        axisMatrix = new Matrixf();
        if (!TryBuildAttachmentModelMatrix(player, target.Config, out axisMatrix)) return false;

        if (target.Pattern != null)
        {
            ApplyModelTransform(axisMatrix, target.Config.Transform);
            if (target.ParentItemTransform != null)
            {
                ApplyModelTransform(axisMatrix, target.ParentItemTransform);
            }
        }

        centerMatrix.Set(axisMatrix.Values);
        ApplyModelTransform(centerMatrix, transform);

        return true;
    }

    private bool TryBuildAttachmentModelMatrix(EntityPlayer player, BackSlingStoredWeaponRenderConfig config, out Matrixf modelMatrix)
    {
        modelMatrix = new Matrixf();
        if (player.Properties?.Client?.Renderer is EntityShapeRenderer renderer)
        {
            modelMatrix.Set(renderer.ModelMat);
        }
        else
        {
            BuildPlayerModelMatrix(modelMatrix, player);
        }

        if (player.AnimManager?.Animator is not AnimatorBase animator ||
            !TryFindPose(animator.RootPoses, config.AttachmentPart, out ElementPose? pose) ||
            pose?.AnimModelMatrix == null)
        {
            return false;
        }

        modelMatrix.Mul(pose.AnimModelMatrix);
        return true;
    }

    private static bool TryFindPose(IEnumerable<ElementPose>? poses, string elementName, out ElementPose? result)
    {
        result = null;
        if (poses == null) return false;

        foreach (ElementPose pose in poses)
        {
            if (string.Equals(pose.ForElement?.Name, elementName, StringComparison.OrdinalIgnoreCase))
            {
                result = pose;
                return true;
            }

            if (TryFindPose(pose.ChildElementPoses, elementName, out result)) return true;
        }

        return false;
    }

    private static void BuildPlayerModelMatrix(Matrixf matrix, EntityPlayer playerEntity)
    {
        matrix.Identity();
        Vec3d camera = playerEntity.CameraPos;
        matrix.Translate(playerEntity.Pos.X - camera.X, playerEntity.Pos.InternalY - camera.Y, playerEntity.Pos.Z - camera.Z);

        float rotX = playerEntity.Properties.Client.Shape?.rotateX ?? 0;
        float rotY = playerEntity.Properties.Client.Shape?.rotateY ?? 0;
        float rotZ = playerEntity.Properties.Client.Shape?.rotateZ ?? 0;

        matrix.Translate(0, playerEntity.SelectionBox.Y2 / 2f, 0);
        matrix.RotateX(playerEntity.Pos.Roll + rotX * GameMath.DEG2RAD);
        matrix.RotateY(playerEntity.BodyYaw + (90f + rotY) * GameMath.DEG2RAD);
        matrix.RotateZ(playerEntity.WalkPitch + rotZ * GameMath.DEG2RAD);
        matrix.Translate(0, -playerEntity.SelectionBox.Y2 / 2f, 0);

        float size = playerEntity.Properties.Client.Size;
        matrix.Scale(size, size, size);
        matrix.Translate(-0.5f, 0, -0.5f);
    }

    private static void ApplyModelTransform(Matrixf matrix, ModelTransform transform)
    {
        FastVec3f scale = transform.ScaleXYZ;
        matrix
            .Translate(transform.Translation.X / 16f, transform.Translation.Y / 16f, transform.Translation.Z / 16f)
            .Translate(transform.Origin.X / 16f, transform.Origin.Y / 16f, transform.Origin.Z / 16f)
            .RotateX(transform.Rotation.X * GameMath.DEG2RAD)
            .RotateY(transform.Rotation.Y * GameMath.DEG2RAD)
            .RotateZ(transform.Rotation.Z * GameMath.DEG2RAD)
            .Scale(scale.X, scale.Y, scale.Z)
            .Translate(-transform.Origin.X / 16f, -transform.Origin.Y / 16f, -transform.Origin.Z / 16f);
    }

    private static Vec3d TransformPoint(Matrixf matrix, Vec3d camera, float x, float y, float z)
    {
        Vec4f transformed = matrix.TransformVector(new Vec4f(x, y, z, 1f));
        return new Vec3d(camera.X + transformed.X, camera.Y + transformed.Y, camera.Z + transformed.Z);
    }

    private void GetCameraBasis(out Vec3d forward, out Vec3d right, out Vec3d up)
    {
        double yaw = _api.World.Player.CameraYaw;
        double pitch = _api.World.Player.CameraPitch;
        forward = new Vec3d(-Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), -Math.Cos(yaw) * Math.Cos(pitch)).Normalize();
        right = new Vec3d(Math.Cos(yaw), 0, -Math.Sin(yaw)).Normalize();
        up = right.Cross(forward).Normalize();
        if (up.LengthSq() < 0.0001) up = new Vec3d(0, 1, 0);
    }

    private static Vec3d GetWorldAxis(GizmoState state, QsOverlayGizmoAxis axis) => axis switch
    {
        QsOverlayGizmoAxis.X => state.AxisX,
        QsOverlayGizmoAxis.Y => state.AxisY,
        QsOverlayGizmoAxis.Z => state.AxisZ,
        _ => state.AxisX
    };

    private static Vec3d Perpendicular(Vec3d direction, Vec3d seed)
    {
        Vec3d perp = direction.Cross(seed);
        if (perp.LengthSq() < 0.0001) perp = direction.Cross(new Vec3d(1, 0, 0));
        if (perp.LengthSq() < 0.0001) perp = direction.Cross(new Vec3d(0, 0, 1));
        return perp.Normalize();
    }

    private static double DistancePointToSegment(double x, double y, Vec2d a, Vec2d b)
    {
        double vx = b.X - a.X;
        double vy = b.Y - a.Y;
        double wx = x - a.X;
        double wy = y - a.Y;
        double lenSq = vx * vx + vy * vy;
        if (lenSq <= 0.0001) return Math.Sqrt(wx * wx + wy * wy);

        double t = Math.Clamp((wx * vx + wy * vy) / lenSq, 0, 1);
        double px = a.X + t * vx;
        double py = a.Y + t * vy;
        double dx = x - px;
        double dy = y - py;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static float NormalizeDegrees(float degrees)
    {
        while (degrees > 180) degrees -= 360;
        while (degrees < -180) degrees += 360;
        return degrees;
    }

    private static Vec3f RotateEulerComponent(Vec3f startRotation, QsOverlayGizmoAxis axis, double deltaDegrees)
    {
        Vec3f result = new(startRotation.X, startRotation.Y, startRotation.Z);
        switch (axis)
        {
            case QsOverlayGizmoAxis.X:
                result.X = NormalizeDegrees(result.X + (float)deltaDegrees);
                break;
            case QsOverlayGizmoAxis.Y:
                result.Y = NormalizeDegrees(result.Y + (float)deltaDegrees);
                break;
            case QsOverlayGizmoAxis.Z:
                result.Z = NormalizeDegrees(result.Z + (float)deltaDegrees);
                break;
        }

        return result;
    }

    private static double Dot(Vec3d left, Vec3d right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;
    private static Vec3d Add(Vec3d left, Vec3d right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    private static Vec3d Sub(Vec3d left, Vec3d right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    private static Vec3d Scale(Vec3d value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    private static Vec3d NormalizeOrDefault(Vec3d value, Vec3d fallback) => value.LengthSq() < 0.000001 ? fallback : value.Normalize();

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _api.Event.MouseDown -= OnMouseDown;
        _api.Event.MouseMove -= OnMouseMove;
        _api.Event.MouseUp -= OnMouseUp;
        _api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }

    private struct GizmoState
    {
        public Vec3d Center;
        public Vec3d AxisX;
        public Vec3d AxisY;
        public Vec3d AxisZ;
        public Vec3d CameraRight;
        public Vec3d CameraUp;
    }
}

internal enum QsOverlayGizmoAxis
{
    None,
    X,
    Y,
    Z
}
