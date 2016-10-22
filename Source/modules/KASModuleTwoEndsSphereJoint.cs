﻿// Kerbal Attachment System
// Mod's author: KospY (http://forum.kerbalspaceprogram.com/index.php?/profile/33868-kospy/)
// Module author: igor.zavoychinskiy@gmail.com
// License: https://github.com/KospY/KAS/blob/master/LICENSE.md

using KASAPIv1;
using System;
using System.Collections;
using UnityEngine;

namespace KAS {

/// <summary>Module that offers a highly configurable setup of three PhysX joints.</summary>
/// <remarks>
/// One spherical joint is located at the source part, another spherical joint is located at the
/// target part. The joints are connected with a third joint that is setup as prismatic. Such setup
/// allows soucre and target parts rotationg relative to each other. Distance between the parts is
/// limited by the prismatic joint.
/// </remarks>
/// <seealso href="http://docs.nvidia.com/gameworks/content/gameworkslibrary/physx/guide/Manual/Joints.html#spherical-joint">
/// PhysX: Spherical joint</seealso>
/// <seealso href="http://docs.nvidia.com/gameworks/content/gameworkslibrary/physx/guide/Manual/Joints.html#prismatic-joint">
/// PhysX: Prismatic joint</seealso>
// TODO(ihsoft): Add an image.
// TODO(ihsoft): Implement prismatic joint linear limits.
// FIXME(ihsoft): Fix initial state setup for the sphere joints.
public sealed class KASModuleTwoEndsSphereJoint : AbstractJointModule, IJointLockState {
  #region Helper class to detect joint breakage
  /// <summary>
  /// Helper class to detect sphere joint ends breakage and deliver event to the host part.
  /// </summary>
  class BrokenJointListener : MonoBehaviour {
    /// <summary>Part to decouple on joint break.</summary>
    public Part host;

    /// <summary>Triggers when joint break force if exceeded.</summary>
    /// <param name="breakForce">Actual force that broke the joint.</param>
    void OnJointBreak(float breakForce) {
      if (host.parent != null) {
        if (gameObject != host.gameObject) {
          host.gameObject.SendMessage(
              "OnJointBreak", breakForce, SendMessageOptions.DontRequireReceiver);
        } else {
          Debug.LogWarning("Owner and host of the joint break listener are the same!");
        }
      }
    }
  }
  #endregion

  #region Part's config fields
  /// <summary>
  /// Config setting. Spring force of the prismatic joint that limits the distance.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This is a <see cref="KSPField"/> annotated field. It's handled by the KSP core and must
  /// <i>not</i> be altered directly. Moreover, in spite of it's declared <c>public</c> it must not
  /// be accessed outside of the module.
  /// </para>
  /// </remarks>
  /// <seealso href="https://kerbalspaceprogram.com/api/class_k_s_p_field.html">
  /// KSP: KSPField</seealso>
  [KSPField]
  public float strutSpringForce = Mathf.Infinity;
  /// <summary>Config setting. Damper force of the spring that limits the distance.</summary>
  /// <remarks>
  /// <para>
  /// This is a <see cref="KSPField"/> annotated field. It's handled by the KSP core and must
  /// <i>not</i> be altered directly. Moreover, in spite of it's declared <c>public</c> it must not
  /// be accessed outside of the module.
  /// </para>
  /// </remarks>
  /// <seealso href="https://kerbalspaceprogram.com/api/class_k_s_p_field.html">
  /// KSP: KSPField</seealso>
  [KSPField]
  public float strutSpringDamperRatio = 0.1f;  // 10% of the force.
  #endregion

  /// <summary>Source sphere joint.</summary>
  /// <remarks>It doesn't allow linear movements but does allow rotation around any axis.</remarks>
  /// <seealso cref="AbstractJointModule.cfgSourceLinkAngleLimit"/>.
  ConfigurableJoint srcJoint;

  /// <summary>Target sphere joint.</summary>
  /// <remarks>It doesn't allow linear movements but does allow rotation around any axis.</remarks>
  /// <seealso cref="AbstractJointModule.cfgTargetLinkAngleLimit"/>
  ConfigurableJoint trgJoint;

  /// <summary>Joint that ties two sphere joints together.</summary>
  /// <remarks>
  /// It doesn't allow rotations but does allow linear movements. Rotations and shrink/stretch
  /// limits are set via config settings.
  /// </remarks>
  /// <seealso cref="strutSpringForce"/>
  /// <seealso cref="AbstractJointModule.cfgMinLinkLength"/>
  /// <seealso cref="AbstractJointModule.cfgMaxLinkLength"/>
  ConfigurableJoint strutJoint;

  /// <summary>Normal state of the source joint. Used to restore from ubreakable state.</summary>
  JointState srcJointState;

  /// <summary>Normal state of the targte joint. Used to restore from ubreakable state.</summary>
  JointState trgJointState;

  /// <summary>Normal state of the strut joint. Used to restore from ubreakable state.</summary>
  JointState strutJointState;

  #region IJointLockState implemenation
  /// <inheritdoc/>
  public bool IsJointUnlocked() {
    return true;
  }
  #endregion
  
  #region ILinkJoint implementation
  /// <inheritdoc/>
  // FIXME(ihsoft): Handle mass!  
  public override void CreateJoint(ILinkSource source, ILinkTarget target) {
    base.CreateJoint(source, target);
    DropStockJoint();  // Stock joint is not used.

    // Create end spherical joints.
    srcJoint = CreateJointEnd(source.attachNode, "KASJointSrc", sourceLinkAngleLimit);
    srcJointState = new JointState().SaveState(srcJoint);
    trgJoint = CreateJointEnd(target.attachNode, "KASJointTrg", targetLinkAngleLimit);
    trgJointState = new JointState().SaveState(trgJoint);
    srcJoint.transform.LookAt(trgJoint.transform);
    trgJoint.transform.LookAt(srcJoint.transform);

    // Link end joints with a prismatic joint.
    strutJoint = srcJoint.gameObject.AddComponent<ConfigurableJoint>();
    KASAPI.JointUtils.ResetJoint(strutJoint);
    strutJoint.connectedBody = trgJoint.GetComponent<Rigidbody>();
    KASAPI.JointUtils.SetupPrismaticJoint(
        strutJoint, springForce: strutSpringForce, springDamperRatio: strutSpringDamperRatio);
    strutJoint.enablePreprocessing = true;
    SetBreakForces(strutJoint, linkBreakForce, Mathf.Infinity);
    strutJointState = new JointState().SaveState(strutJoint);
  }

  /// <inheritdoc/>
  public override void DropJoint() {
    base.DropJoint();
    UnityEngine.Object.Destroy(srcJoint);
    srcJoint = null;
    UnityEngine.Object.Destroy(trgJoint);
    trgJoint = null;
    strutJoint = null;
  }

  /// <inheritdoc/>
  public override void AdjustJoint(bool isUnbreakable = false) {
    if (isUnbreakable) {
      SetupUnbreakableJoint(srcJoint);
      SetupUnbreakableJoint(trgJoint);
      SetupUnbreakableJoint(strutJoint);
    } else {
      srcJointState.RestoreState(srcJoint);
      trgJointState.RestoreState(trgJoint);
      strutJointState.RestoreState(strutJoint);
    }
  }
  #endregion

  #region Private utility methods
  /// <summary>Creates a game object joined with the attach node via a spherical joint.</summary>
  /// <remarks>
  /// Joint object will be aligned exactly to the attach node. This will result in zero anchor nodes
  /// and zero/identity relative position and rotation. Caller needs to adjust position/rotation of
  /// the created object as needed.
  /// <para>
  /// Joint object will have rigidobject created. Its physical settings will be default. Caller may
  /// need to adjust the properties.
  /// </para>
  /// </remarks>
  /// <param name="an">Node to attach new joint to.</param>
  /// <param name="objName">Name of the game object for the joint.</param>
  /// <param name="angleLimit">Degree of freedom for the joint.</param>
  /// <returns>Joint object.</returns>
  ConfigurableJoint CreateJointEnd(AttachNode an, string objName, float angleLimit) {
    var jointObj = new GameObject(objName);
    jointObj.transform.position = an.nodeTransform.position;
    jointObj.transform.rotation = an.nodeTransform.rotation;
    jointObj.AddComponent<BrokenJointListener>().host = part;
    jointObj.AddComponent<Rigidbody>();
    var joint = jointObj.AddComponent<ConfigurableJoint>();
    KASAPI.JointUtils.ResetJoint(joint);
    KASAPI.JointUtils.SetupSphericalJoint(joint, angleLimit: angleLimit);
    joint.enablePreprocessing = true;
    joint.connectedBody = an.owner.rb;
    SetBreakForces(joint, linkBreakForce, linkBreakTorque);
    return joint;
  }
  #endregion
}

}  // namespace
