using System;
using CvBase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AuthModeProbe = global::CvServer.Handlers.AuthModeProbe;

namespace Tests.CvServer;

/// <summary>
/// サーバの認証モード判定（<see cref="AuthModeProbe"/>）の単体テスト。
/// [UatChangeable] 直後の属性が [Authorize] か [AllowAnonymous] かで判定が変わることを確認する。
/// </summary>
[TestClass]
public class AuthModeProbeTests {
	sealed class AllAuthorize {
		[UatChangeable]
		[Authorize]
		public void A() { }
		[UatChangeable]
		[Authorize]
		public void B() { }
	}

	sealed class AllAnonymous {
		[UatChangeable]
		[AllowAnonymous]
		public void A() { }
		[UatChangeable]
		[AllowAnonymous]
		public void B() { }
		// 切替対象外の匿名メソッドは判定に影響しない
		[AllowAnonymous]
		public void Login() { }
	}

	sealed class Mixed {
		[UatChangeable]
		[AllowAnonymous]
		public void A() { }
		[UatChangeable]
		[Authorize]
		public void B() { }
	}

	sealed class NoTarget {
		[Authorize]
		public void A() { }
	}

	[TestMethod]
	public void 全てAuthorizeならログイン必須() {
		Assert.IsTrue(AuthModeProbe.IsAuthRequired(new[] { typeof(AllAuthorize) }));
	}

	[TestMethod]
	public void 全てAllowAnonymousならログイン不要() {
		Assert.IsFalse(AuthModeProbe.IsAuthRequired(new[] { typeof(AllAnonymous) }));
	}

	[TestMethod]
	public void 一部だけ切替ならログイン必須() {
		Assert.IsTrue(AuthModeProbe.IsAuthRequired(new[] { typeof(Mixed) }));
	}

	[TestMethod]
	public void 対象メソッドが無ければログイン必須() {
		Assert.IsTrue(AuthModeProbe.IsAuthRequired(new[] { typeof(NoTarget) }));
	}

	[TestMethod]
	public void 通常ビルドのサーバはログイン必須() {
		// 認証解除(change4uat_run.ps1)中のソースでは失敗する。解除中はコミットしない運用のため通常状態を期待値とする
		Assert.IsTrue(AuthModeProbe.IsAuthRequired(typeof(AuthModeProbe).Assembly));
	}
}
