﻿using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class BVHNode
{
    public AABB aabb;
    public BVHNode left;
    public BVHNode right;

    public BVHNode(AABB aabb)
    {
        this.aabb = aabb;
        left = null;
        right = null;
    }

    public BVHNode()
    {
        
    }
}

public class LinearBVHNode
{
    public AABB aabb;
    public bool left;
    public int right;
}

public class BVHBucket
{
    public AABB aabb;
    public int count;

    public BVHBucket()
    {
        aabb = AABB.Default;
        count = 0;
    }
}

//默认构造AABB BVH 
public class BVH
{
    private Transform transform;
    private BVHSplitMethod bvhSplitMethod;
    private LinearBVHNode[] bvhNodes;

    //transform为null则构建基于场景的粗略测试bvh, 不为null则构建基于对象的精细测试bvh
    public BVH(Transform transform, BVHSplitMethod bvhSplitMethod)
    {
        this.transform = transform;
        this.bvhSplitMethod = bvhSplitMethod;
    }

    public void CreateBVH()
    {
        if (transform == null)
        {
            MeshFilter[] meshFilters = GameObject.FindObjectsOfType<MeshFilter>();
            AABB[] aabbs = new AABB[meshFilters.Length];
            for (int i = 0; i < meshFilters.Length; i++)
            {
                AABB aabb = new AABB(meshFilters[i].transform);
                aabb.UpdateAABB(AABBStructureMode.Compact);
                aabbs[i] = aabb;
            }

            int count = 0;
            BVHNode root = BuildBVH(aabbs, 0, aabbs.Length, ref count);
            FlattenBVH(root, count);
        }
    }

//    void TestBVH2(ref string res)
//    {
//        Stack<int> s = new Stack<int>();
//        s.Push(0);
//        while (s.Count > 0)
//        {
//            int idx = s.Pop();
//            res += bvhNodes[idx].aabb.GetCentroid() + " ";
//            if (bvhNodes[idx].right != -1)
//            {
//                s.Push(bvhNodes[idx].right);
//            }
//
//            if (bvhNodes[idx].left)
//            {
//                s.Push(++idx);
//            }
//        }
//    }

    void FlattenBVH(BVHNode root, int count)
    {
        bvhNodes = new LinearBVHNode[count];
        for (int i = 0; i < count; i++)
        {
            bvhNodes[i] = new LinearBVHNode();
        }

        int offset = 0;
        BuildFlattenBVH(root, ref offset);
    }

    void BuildFlattenBVH(BVHNode bvhNode, ref int offset)
    {
        LinearBVHNode linearBVHNode = new LinearBVHNode();
        linearBVHNode.aabb = bvhNode.aabb;
        bvhNodes[offset] = linearBVHNode;
        if (bvhNode.left == null && bvhNode.right == null)
        {
            offset++;
            linearBVHNode.left = false;
            linearBVHNode.right = -1;
        }
        else
        {
            if (bvhNode.left != null)
            {
                linearBVHNode.left = true;
                offset++;
                BuildFlattenBVH(bvhNode.left, ref offset);
                linearBVHNode.right = offset;
            }

            if (bvhNode.right != null)
            {
                BuildFlattenBVH(bvhNode.right, ref offset);
            }
        }
    }

    BVHNode BuildBVH(AABB[] aabbs, int start, int end, ref int count)
    {
        AABB aabb = AABB.Default;
        for (int i = start; i < end; i++)
        {
            aabb.Union(aabbs[i]);
        }

        count++;
        if (start == end - 1)
        {
            return new BVHNode(aabbs[start]);
        }
        else
        {
            BVHNode bvhNode = new BVHNode();
            AABB centroidAABB = AABB.Default;
            for (int i = start; i < end; i++)
            {
                centroidAABB.Union(aabbs[i].GetCentroid());
            }

            int dim = centroidAABB.MaximumExtent();
            int split = 0;
            float splitCentroid = 0;
            if (Mathf.Abs(centroidAABB.transformMax[dim] - centroidAABB.transformMin[dim]) <= float.Epsilon)
            {
                split = (start + end) / 2;
            }
            else
            {
                //质心中点分割方法
                if (bvhSplitMethod == BVHSplitMethod.SplitMiddle)
                {
                    float pivot = centroidAABB.GetCentroid()[dim];
                    int left = start;
                    int right = end - 1;
                    do
                    {
                        while (pivot > aabbs[left].GetCentroid()[dim])
                        {
                            left++;
                        }

                        while (pivot < aabbs[right].GetCentroid()[dim])
                        {
                            right--;
                        }

                        if (left > right)
                        {
                            break;
                        }

                        if (left < right)
                        {
                            Swap(aabbs, left, right);
                        }

                        left++;
                        right--;
                    } while (left <= right);

                    split = left;
                    splitCentroid = pivot;
                }
                //等尺寸集分割方法
                else if (bvhSplitMethod == BVHSplitMethod.SplitEqualCounts)
                {
                    split = (start + end) / 2;
                    QuickSelect(aabbs, split, start, end - 1, dim);
                    splitCentroid = aabbs[split].GetCentroid()[dim];
                }
                //启发式表面积方法
                else if (bvhSplitMethod == BVHSplitMethod.SplitSAH)
                {
                    if (end - start <= 4)
                    {
                        split = (start + end) / 2;
                        QuickSelect(aabbs, split, start, end - 1, dim);
                        splitCentroid = aabbs[split].GetCentroid()[dim];
                    }
                    else
                    {
                        int splitBucket = 12;
                        BVHBucket[] buckets = new BVHBucket[splitBucket];
                        for (int i = 0; i < splitBucket; i++)
                        {
                            buckets[i] = new BVHBucket();
                        }

                        for (int i = start; i < end; i++)
                        {
                            int bucketNo = GetBucketNo(aabbs[i], centroidAABB, dim, splitBucket);
                            buckets[bucketNo].count++;
                            buckets[bucketNo].aabb.Union(aabbs[i]);
                        }

                        float[] cost = new float[splitBucket - 1];
                        for (int i = 0; i < splitBucket - 1; i++)
                        {
                            AABB aabb1 = AABB.Default;
                            AABB aabb2 = AABB.Default;
                            int count1 = 0;
                            int count2 = 0;
                            for (int j = 0; j <= i; j++)
                            {
                                aabb1.Union(buckets[j].aabb);
                                count1 += buckets[j].count;
                            }

                            for (int j = i + 1; j < splitBucket; j++)
                            {
                                aabb2.Union(buckets[j].aabb);
                                count2 += buckets[j].count;
                            }

                            cost[i] = 0.125f + (aabb1.GetSurfaceArea() * count1 + aabb2.GetSurfaceArea() * count2) /
                                      aabb.GetSurfaceArea();
                        }

                        float minCost = cost[0];
                        int pivot = 0;
                        for (int i = 1; i < splitBucket - 1; i++)
                        {
                            if (cost[i] < minCost)
                            {
                                minCost = cost[i];
                                pivot = i;
                            }
                        }

                        int left = start;
                        int right = end - 1;
                        do
                        {
                            while (pivot >= GetBucketNo(aabbs[left], centroidAABB, dim, splitBucket))
                            {
                                left++;
                            }

                            while (pivot < GetBucketNo(aabbs[right], centroidAABB, dim, splitBucket))
                            {
                                right--;
                            }

                            if (left > right)
                            {
                                break;
                            }

                            if (left < right)
                            {
                                Swap(aabbs, left, right);
                            }

                            left++;
                            right--;
                        } while (left <= right);

                        split = left;

                        splitCentroid = centroidAABB.transformMin[dim] + (pivot + 1.0f) / splitBucket *
                                        (centroidAABB.transformMax[dim] - centroidAABB.transformMin[dim]);
                    }
                }

                //断言检验
                for (int i = start; i < split; i++)
                {
                    Assert.IsTrue(aabbs[i].GetCentroid()[dim] <= splitCentroid);
                }

                for (int i = split; i < end; i++)
                {
                    Assert.IsTrue(aabbs[i].GetCentroid()[dim] >= splitCentroid);
                }
            }

            bvhNode.left = BuildBVH(aabbs, start, split, ref count);
            bvhNode.right = BuildBVH(aabbs, split, end, ref count);
            bvhNode.aabb = AABB.Default;
            bvhNode.aabb.Union(bvhNode.left.aabb);
            bvhNode.aabb.Union(bvhNode.right.aabb);
            return bvhNode;
        }
    }

    int GetBucketNo(AABB aabb, AABB centroidAABB, int dim, int splitBucket)
    {
        int bucketNo = (int) ((aabb.GetCentroid()[dim] - centroidAABB.transformMin[dim]) /
                              (centroidAABB.transformMax[dim] - centroidAABB.transformMin[dim]) *
                              splitBucket);
        if (bucketNo == splitBucket)
        {
            bucketNo--;
        }

        return bucketNo;
    }

    void QuickSelect(AABB[] aabbs, int k, int left, int right, int dim)
    {
        float pivot = Median3(aabbs, left, right, dim);
        int i = left;
        int j = right;
        do
        {
            while (pivot > aabbs[left].GetCentroid()[dim])
            {
                left++;
            }

            while (pivot < aabbs[right].GetCentroid()[dim])
            {
                right--;
            }

            if (left > right)
            {
                break;
            }

            if (left < right)
            {
                Swap(aabbs, left, right);
            }

            left++;
            right--;
        } while (left <= right);

        if (k <= right)
        {
            QuickSelect(aabbs, k, i, right, dim);
        }
        else if (k >= left)
        {
            QuickSelect(aabbs, k, left, j, dim);
        }
    }

    void Swap(AABB[] aabbs, int i, int j)
    {
        AABB tmp = aabbs[i];
        aabbs[i] = aabbs[j];
        aabbs[j] = tmp;
    }

    float Median3(AABB[] aabbs, int left, int right, int dim)
    {
        int mid = (left + right) / 2;
        if (aabbs[left].GetCentroid()[dim] > aabbs[mid].GetCentroid()[dim])
        {
            Swap(aabbs, left, mid);
        }

        if (aabbs[left].GetCentroid()[dim] > aabbs[right].GetCentroid()[dim])
        {
            Swap(aabbs, left, right);
        }

        if (aabbs[mid].GetCentroid()[dim] > aabbs[right].GetCentroid()[dim])
        {
            Swap(aabbs, mid, right);
        }

        return aabbs[mid].GetCentroid()[dim];
    }

    public void DrawBVH()
    {
        for (int i = 0; i < bvhNodes.Length; i++)
        {
            bvhNodes[i].aabb.DrawAABB();
        }
    }
}