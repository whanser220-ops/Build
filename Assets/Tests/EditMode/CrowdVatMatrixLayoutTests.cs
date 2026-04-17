using NUnit.Framework;
using UnityEngine;

public class CrowdVatMatrixLayoutTests
{
    [Test]
    public void VatTextureRowDecodeMatchesMatrixMultiplyPoint()
    {
        Matrix4x4 matrix = Matrix4x4.TRS(
            new Vector3(1.25f, -0.75f, 2.5f),
            Quaternion.Euler(18.0f, -33.0f, 11.0f),
            new Vector3(1.1f, 0.9f, 1.05f));
        Vector3 point = new Vector3(0.42f, 1.18f, -0.37f);

        Vector4 row0 = new Vector4(matrix.m00, matrix.m01, matrix.m02, matrix.m03);
        Vector4 row1 = new Vector4(matrix.m10, matrix.m11, matrix.m12, matrix.m13);
        Vector4 row2 = new Vector4(matrix.m20, matrix.m21, matrix.m22, matrix.m23);

        Vector3 decoded = TransformVatPosition(row0, row1, row2, point);
        Vector3 expected = matrix.MultiplyPoint3x4(point);

        AssertVectorsApproximatelyEqual(expected, decoded);
    }

    [Test]
    public void IndirectColumnDecodeMatchesMatrixMultiplyPoint()
    {
        Matrix4x4 matrix = Matrix4x4.TRS(
            new Vector3(-3.2f, 0.5f, 4.1f),
            Quaternion.Euler(-7.0f, 122.0f, 0.0f),
            new Vector3(0.98f, 1.0f, 1.03f));
        Vector3 point = new Vector3(-0.66f, 0.24f, 0.91f);

        Vector4 column0 = new Vector4(matrix.m00, matrix.m10, matrix.m20, matrix.m03);
        Vector4 column1 = new Vector4(matrix.m01, matrix.m11, matrix.m21, matrix.m13);
        Vector4 column2 = new Vector4(matrix.m02, matrix.m12, matrix.m22, matrix.m23);

        Vector3 decoded = TransformColumnMatrixPosition(column0, column1, column2, point);
        Vector3 expected = matrix.MultiplyPoint3x4(point);

        AssertVectorsApproximatelyEqual(expected, decoded);
    }

    private static Vector3 TransformVatPosition(Vector4 row0, Vector4 row1, Vector4 row2, Vector3 position)
    {
        Vector4 homogeneousPosition = new Vector4(position.x, position.y, position.z, 1.0f);
        return new Vector3(
            Vector4.Dot(row0, homogeneousPosition),
            Vector4.Dot(row1, homogeneousPosition),
            Vector4.Dot(row2, homogeneousPosition));
    }

    private static Vector3 TransformColumnMatrixPosition(Vector4 column0, Vector4 column1, Vector4 column2, Vector3 position)
    {
        Vector3 axisX = new Vector3(column0.x, column0.y, column0.z);
        Vector3 axisY = new Vector3(column1.x, column1.y, column1.z);
        Vector3 axisZ = new Vector3(column2.x, column2.y, column2.z);
        return axisX * position.x
            + axisY * position.y
            + axisZ * position.z
            + new Vector3(column0.w, column1.w, column2.w);
    }

    private static void AssertVectorsApproximatelyEqual(Vector3 expected, Vector3 actual)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5f));
    }
}
