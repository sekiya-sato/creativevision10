using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.Util;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels;

public partial class MasterMeishoMenteViewModel : ObservableObject {
	[ObservableProperty]
	string title = "Name Master Maintenance";

	[ObservableProperty]
	ObservableCollection<MasterMeishoRow> listData = [];

	[ObservableProperty]
	MasterMeishoRow? current;

	[ObservableProperty]
	MasterMeishoRow currentEdit = new();

	[ObservableProperty]
	int count;

	[ObservableProperty]
	string? desc0;

	[RelayCommand]
	void Init() {
		LoadSampleData();
	}

	[RelayCommand]
	void Exit() {
		ClientLib.Exit(this);
	}

	[RelayCommand]
	void DoList() {
		LoadSampleData();
	}

	[RelayCommand]
	void DoInsert() {
		var newItem = CurrentEdit.Clone();
		if (newItem.Id == 0) {
			newItem.Id = NextId();
		}
		newItem.VdateC = DateTime.Now;
		newItem.VdateU = DateTime.Now;
		ListData.Add(newItem);
		Count = ListData.Count;
		Current = newItem;
	}

	[RelayCommand]
	void DoUpdate() {
		if (Current == null) {
			MessageBox.Show("Select a record to update.", Title);
			return;
		}
		Current.CopyFrom(CurrentEdit);
		Current.VdateU = DateTime.Now;
	}

	[RelayCommand]
	void DoDelete() {
		if (Current == null) {
			MessageBox.Show("Select a record to delete.", Title);
			return;
		}
		var target = Current;
		var next = ListData.SkipWhile(x => x != target).Skip(1).FirstOrDefault()
			?? ListData.TakeWhile(x => x != target).LastOrDefault();
		ListData.Remove(target);
		Count = ListData.Count;
		Current = next;
		if (Current == null) {
			CurrentEdit = new MasterMeishoRow();
		}
	}

	[RelayCommand]
	void DoSelKubun() {
		CurrentEdit.Id_Kubun = 100;
		CurrentEdit.Disp0 = "Demo Category";
	}

	[RelayCommand]
	void DoOutputJson() {
		var dir = ClientLib.GetDataDir();
		var path = Path.Combine(dir, "MasterMeishoSample.json");
		var json = JsonConvert.SerializeObject(ListData, Formatting.Indented);
		File.WriteAllText(path, json);
		MessageBox.Show($"Exported JSON.\n{path}", Title);
	}

	void LoadSampleData() {
		var now = DateTime.Now;
		var items = Enumerable.Range(1, 8).Select(i => new MasterMeishoRow {
			Id = i,
			Id_Kubun = 10 + i % 3,
			Kubun = $"CAT{i % 3 + 1:00}",
			Code = $"CD{i:0000}",
			Name = $"Sample Name {i}",
			Desc0 = $"Note {i}",
			SortNo = i * 10,
			Disp0 = $"ÉJÉeÉSÉäÅ[{i % 3 + 1}",
			VdateC = now.AddDays(-i),
			VdateU = now
		}).ToList();

		ListData = new ObservableCollection<MasterMeishoRow>(items);
		Count = ListData.Count;
		Desc0 = $"Sample {Count} items";
		Current = ListData.FirstOrDefault();
		if (Current == null) {
			CurrentEdit = new MasterMeishoRow();
		}
	}

	long NextId() => ListData.Count == 0 ? 1 : ListData.Max(x => x.Id) + 1;

	partial void OnCurrentChanged(MasterMeishoRow? value) {
		CurrentEdit = value?.Clone() ?? new MasterMeishoRow();
	}
}

public partial class MasterMeishoRow : ObservableObject {
	[ObservableProperty]
	long id;

	[ObservableProperty]
	long id_Kubun;

	[ObservableProperty]
	string? kubun;

	[ObservableProperty]
	string? code;

	[ObservableProperty]
	string? name;

	[ObservableProperty]
	string? desc0;

	[ObservableProperty]
	string? disp0;

	[ObservableProperty]
	int sortNo;

	[ObservableProperty]
	DateTime vdateC = DateTime.Now;

	[ObservableProperty]
	DateTime vdateU = DateTime.Now;

	public MasterMeishoRow Clone() {
		return new MasterMeishoRow {
			Id = Id,
			Id_Kubun = Id_Kubun,
			Kubun = Kubun,
			Code = Code,
			Name = Name,
			Desc0 = Desc0,
			Disp0 = Disp0,
			SortNo = SortNo,
			VdateC = VdateC,
			VdateU = VdateU
		};
	}

	public void CopyFrom(MasterMeishoRow source) {
		Id = source.Id;
		Id_Kubun = source.Id_Kubun;
		Kubun = source.Kubun;
		Code = source.Code;
		Name = source.Name;
		Desc0 = source.Desc0;
		Disp0 = source.Disp0;
		SortNo = source.SortNo;
		VdateC = source.VdateC;
		VdateU = source.VdateU;
	}
}
