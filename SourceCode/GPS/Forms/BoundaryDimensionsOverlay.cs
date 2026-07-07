using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private void DrawBoundaryDimensionsOverlay()
        {
            if (bnd.bndList.Count == 0) return;
            if (bnd.bndList[0].fenceLine == null || bnd.bndList[0].fenceLine.Count < 3) return;

            if (!TryGetBoundaryDimensions(
                bnd.bndList[0].fenceLine,
                out double length,
                out double width,
                out double lengthLabelEast,
                out double lengthLabelNorth,
                out double widthLabelEast,
                out double widthLabelNorth))
            {
                return;
            }

            string lengthText = "Length: " + FormatBoundaryDistance(length);
            string widthText = "Wide: " + FormatBoundaryDistance(width);

            GL.Color3(1.0f, 1.0f, 0.2f);
            font.DrawText3D(lengthLabelEast, lengthLabelNorth, lengthText, camHeading, 0.9);
            font.DrawText3D(widthLabelEast, widthLabelNorth, widthText, camHeading, 0.9);
        }

        private string FormatBoundaryDistance(double meters)
        {
            if (isMetric)
            {
                return meters >= 100.0
                    ? meters.ToString("N0") + " m"
                    : meters.ToString("N1") + " m";
            }

            double feet = meters * glm.m2ft;
            return feet >= 100.0
                ? feet.ToString("N0") + " ft"
                : feet.ToString("N1") + " ft";
        }

        private static bool TryGetBoundaryDimensions(
            List<vec3> points,
            out double length,
            out double width,
            out double lengthLabelEast,
            out double lengthLabelNorth,
            out double widthLabelEast,
            out double widthLabelNorth)
        {
            length = 0.0;
            width = 0.0;
            lengthLabelEast = 0.0;
            lengthLabelNorth = 0.0;
            widthLabelEast = 0.0;
            widthLabelNorth = 0.0;

            double bestArea = double.MaxValue;
            double bestMinAlong = 0.0, bestMaxAlong = 0.0;
            double bestMinAcross = 0.0, bestMaxAcross = 0.0;
            double bestAxisEast = 1.0, bestAxisNorth = 0.0;
            double bestAcrossEast = 0.0, bestAcrossNorth = 1.0;

            for (int i = 0; i < points.Count; i++)
            {
                vec3 a = points[i];
                vec3 b = points[(i + 1) % points.Count];
                double edgeEast = b.easting - a.easting;
                double edgeNorth = b.northing - a.northing;
                double edgeLength = Math.Sqrt((edgeEast * edgeEast) + (edgeNorth * edgeNorth));
                if (edgeLength < 0.01) continue;

                double axisEast = edgeEast / edgeLength;
                double axisNorth = edgeNorth / edgeLength;
                double acrossEast = -axisNorth;
                double acrossNorth = axisEast;

                double minAlong = double.MaxValue;
                double maxAlong = double.MinValue;
                double minAcross = double.MaxValue;
                double maxAcross = double.MinValue;

                foreach (vec3 point in points)
                {
                    double along = (point.easting * axisEast) + (point.northing * axisNorth);
                    double across = (point.easting * acrossEast) + (point.northing * acrossNorth);

                    if (along < minAlong) minAlong = along;
                    if (along > maxAlong) maxAlong = along;
                    if (across < minAcross) minAcross = across;
                    if (across > maxAcross) maxAcross = across;
                }

                double alongSpan = maxAlong - minAlong;
                double acrossSpan = maxAcross - minAcross;
                double area = alongSpan * acrossSpan;

                if (area < bestArea)
                {
                    bestArea = area;
                    bestMinAlong = minAlong;
                    bestMaxAlong = maxAlong;
                    bestMinAcross = minAcross;
                    bestMaxAcross = maxAcross;
                    bestAxisEast = axisEast;
                    bestAxisNorth = axisNorth;
                    bestAcrossEast = acrossEast;
                    bestAcrossNorth = acrossNorth;
                }
            }

            if (bestArea == double.MaxValue) return false;

            double spanAlong = bestMaxAlong - bestMinAlong;
            double spanAcross = bestMaxAcross - bestMinAcross;
            bool alongIsLength = spanAlong >= spanAcross;

            length = alongIsLength ? spanAlong : spanAcross;
            width = alongIsLength ? spanAcross : spanAlong;

            double centerAlong = (bestMinAlong + bestMaxAlong) * 0.5;
            double centerAcross = (bestMinAcross + bestMaxAcross) * 0.5;
            double outsideOffset = Math.Max(8.0, width * 0.10);

            if (alongIsLength)
            {
                lengthLabelEast = (centerAlong * bestAxisEast) + ((bestMaxAcross + outsideOffset) * bestAcrossEast);
                lengthLabelNorth = (centerAlong * bestAxisNorth) + ((bestMaxAcross + outsideOffset) * bestAcrossNorth);
                widthLabelEast = ((bestMaxAlong + outsideOffset) * bestAxisEast) + (centerAcross * bestAcrossEast);
                widthLabelNorth = ((bestMaxAlong + outsideOffset) * bestAxisNorth) + (centerAcross * bestAcrossNorth);
            }
            else
            {
                lengthLabelEast = ((bestMaxAlong + outsideOffset) * bestAxisEast) + (centerAcross * bestAcrossEast);
                lengthLabelNorth = ((bestMaxAlong + outsideOffset) * bestAxisNorth) + (centerAcross * bestAcrossNorth);
                widthLabelEast = (centerAlong * bestAxisEast) + ((bestMaxAcross + outsideOffset) * bestAcrossEast);
                widthLabelNorth = (centerAlong * bestAxisNorth) + ((bestMaxAcross + outsideOffset) * bestAcrossNorth);
            }

            return length > 0.01 && width > 0.01;
        }
    }
}
