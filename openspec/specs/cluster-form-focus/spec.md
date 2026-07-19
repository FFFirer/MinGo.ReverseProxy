# cluster-form-focus Specification

## Purpose
Define focus behavior for the cluster management form, including the cluster name/ID input.

## Requirements

### Requirement: 目标地址输入框保持焦点

在集群管理表单（ClusterFormModal）中，用户输入目标地址时，输入框 SHALL 保持焦点不丢失。

#### Scenario: 输入目标地址保持焦点
- **WHEN** 用户在「目标地址」输入框中输入字符
- **THEN** 输入框保持焦点，不因列表重新渲染而失去焦点

#### Scenario: 添加目标行后输入焦点
- **WHEN** 用户点击「添加目标」按钮新增一行目标地址
- **THEN** 新增行的输入框可正常获得焦点并进行输入

#### Scenario: 删除目标行后其他输入框正常
- **WHEN** 用户删除一行目标地址
- **THEN** 剩余行的输入框焦点行为正常

## ADDED Requirements

### Requirement: Cluster form uses Id only

The cluster form SHALL have a single "集群名称" input that serves as both the name and the ID. There SHALL NOT be a separate hidden ID field (no GUID).

#### Scenario: Create form
- **WHEN** user opens the "add cluster" form
- **THEN** there SHALL be one text input: "集群名称" (which becomes the cluster's Id)
- **AND** there SHALL NOT be a separately stored GUID

#### Scenario: Edit form
- **WHEN** user opens the "edit cluster" form
- **THEN** the cluster Id SHALL be displayed as read-only text (cannot be changed)
- **AND** the form SHALL NOT contain a name input separate from Id
