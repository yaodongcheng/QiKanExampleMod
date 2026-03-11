#!/usr/bin/env python3
"""
Git 图形化快捷工具 - 完整版
"""

import tkinter as tk
from tkinter import messagebox, simpledialog, scrolledtext, ttk
import subprocess
import sys
import os

# 切换到项目目录
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# Git 路径（根据您的系统调整）
# 如果 git 命令可以直接使用，设置为 None ，这就是终极的Git工具
GIT_PATH = None


def run_git(cmd):
    """运行 git 命令"""
    if GIT_PATH:
        full_cmd = cmd.replace('git ', f'"{GIT_PATH}" ', 1)
    else:
        full_cmd = cmd
    result = subprocess.run(full_cmd, shell=True, capture_output=True)
    stdout = result.stdout.decode('utf-8', errors='ignore') if result.stdout else ""
    stderr = result.stderr.decode('utf-8', errors='ignore') if result.stderr else ""
    return result.returncode == 0, stdout, stderr


def get_file_status():
    """获取文件状态，返回（暂存区列表，工作区列表，未跟踪列表）"""
    staged_files = []
    modified_files = []
    untracked_files = []

    # 获取暂存区的文件
    success, stdout, _ = run_git("git diff --cached --name-only")
    if success and stdout:
        staged_files = [line.strip() for line in stdout.strip().split('\n') if line.strip()]

    # 获取工作区已修改但未暂存的文件
    success, stdout, _ = run_git("git diff --name-only")
    if success and stdout:
        modified_files = [line.strip() for line in stdout.strip().split('\n') if line.strip()]

    # 获取未跟踪的文件
    success, stdout, _ = run_git("git ls-files --others --exclude-standard")
    if success and stdout:
        untracked_files = [line.strip() for line in stdout.strip().split('\n') if line.strip()]

    return staged_files, modified_files, untracked_files


def get_status():
    """获取当前状态（用于检查是否有变化）"""
    staged, modified, untracked = get_file_status()
    return len(staged) > 0 or len(modified) > 0 or len(untracked) > 0


def get_branch():
    """获取当前分支"""
    success, stdout, _ = run_git("git branch --show-current")
    if success:
        return stdout.strip()
    return "main"


def show_file_details():
    """显示文件详情窗口"""
    staged, modified, untracked = get_file_status()

    detail_window = tk.Toplevel()
    detail_window.title("文件状态详情")
    detail_window.geometry("600x400")

    # 创建标签页
    notebook = ttk.Notebook(detail_window)
    notebook.pack(expand=True, fill='both', padx=10, pady=10)

    # 已暂存文件
    if staged:
        staged_frame = tk.Frame(notebook)
        notebook.add(staged_frame, text=f"已暂存 ({len(staged)})")

        text_area = scrolledtext.ScrolledText(staged_frame, wrap=tk.WORD, font=("Consolas", 9))
        text_area.pack(expand=True, fill='both', padx=5, pady=5)
        for f in staged:
            text_area.insert(tk.END, f"✓ {f}\n")
        text_area.config(state=tk.DISABLED)
    else:
        notebook.add(tk.Label(notebook, text="没有已暂存的文件", padx=20, pady=20), text="已暂存 (0)")

    # 已修改未暂存
    if modified:
        modified_frame = tk.Frame(notebook)
        notebook.add(modified_frame, text=f"已修改 ({len(modified)})")

        text_area = scrolledtext.ScrolledText(modified_frame, wrap=tk.WORD, font=("Consolas", 9))
        text_area.pack(expand=True, fill='both', padx=5, pady=5)
        for f in modified:
            text_area.insert(tk.END, f"⚡ {f}\n")
        text_area.config(state=tk.DISABLED)
    else:
        notebook.add(tk.Label(notebook, text="没有已修改的文件", padx=20, pady=20), text="已修改 (0)")

    # 未跟踪文件
    if untracked:
        untracked_frame = tk.Frame(notebook)
        notebook.add(untracked_frame, text=f"未跟踪 ({len(untracked)})")

        text_area = scrolledtext.ScrolledText(untracked_frame, wrap=tk.WORD, font=("Consolas", 9))
        text_area.pack(expand=True, fill='both', padx=5, pady=5)
        for f in untracked:
            text_area.insert(tk.END, f"? {f}\n")
        text_area.config(state=tk.DISABLED)
    else:
        notebook.add(tk.Label(notebook, text="没有未跟踪的文件", padx=20, pady=20), text="未跟踪 (0)")

    tk.Button(detail_window, text="关闭", command=detail_window.destroy).pack(pady=5)


def add_to_staging():
    """添加所有更改到暂存区"""
    staged, modified, untracked = get_file_status()

    if not modified and not untracked:
        messagebox.showinfo("提示", "没有需要添加的文件")
        return

    run_git("git add -A")
    messagebox.showinfo("成功", "已添加所有更改到暂存区")
    update_file_list()


def clear_staging():
    """清空暂存区"""
    staged, modified, untracked = get_file_status()

    if not staged:
        messagebox.showinfo("提示", "暂存区为空")
        return

    run_git("git reset HEAD")
    messagebox.showinfo("成功", "已清空暂存区")
    update_file_list()


def commit(push_after=False):
    """提交更改"""
    staged, modified, untracked = get_file_status()

    # 检查是否有文件需要提交
    if not staged and not modified and not untracked:
        messagebox.showinfo("提示", "没有需要提交的更改")
        return False

    # 自动将所有更改添加到暂存区（简化操作）
    if modified or untracked:
        run_git("git add -A")

    # 询问提交信息
    msg = simpledialog.askstring("提交", "输入提交信息:", initialvalue="更新代码")
    if not msg:
        return False

    success, stdout, stderr = run_git(f'git commit -m "{msg}"')
    if success:
        if push_after:
            return push()
        else:
            messagebox.showinfo("成功", "本地提交成功！\n记得点击'推送'上传到 GitHub")
            update_file_list()
            update_status_label()
            return True
    else:
        messagebox.showerror("错误", f"提交失败:\n{stderr}")
        return False


def push():
    """推送到远程"""
    branch = get_branch()
    success, stdout, stderr = run_git(f"git push origin {branch}")
    if success:
        messagebox.showinfo("成功", f"已推送到 GitHub！\n分支: {branch}")
        return True
    else:
        # 检查是否需要设置上游分支
        if "no upstream branch" in stderr.lower():
            success, stdout, stderr = run_git(f"git push -u origin {branch}")
            if success:
                messagebox.showinfo("成功", f"已推送到 GitHub！\n分支: {branch}")
                return True
        messagebox.showerror("错误", f"推送失败:\n{stderr}")
        return False


def commit_and_push():
    """提交并推送"""
    if commit(push_after=True):
        update_status_label()


def pull():
    """从远程拉取"""
    branch = get_branch()
    success, stdout, stderr = run_git(f"git pull origin {branch}")
    if success:
        messagebox.showinfo("成功", f"已从 GitHub 拉取最新代码！\n分支: {branch}")
        update_status_label()
        return True
    else:
        messagebox.showerror("错误", f"拉取失败:\n{stderr}")
        return False


def show_log():
    """显示提交历史，支持回退到指定版本"""
    success, stdout, _ = run_git("git log --oneline -20 --graph")
    if not success:
        messagebox.showerror("错误", "无法获取提交历史")
        return

    # 解析提交历史，提取提交哈希和消息
    commits = []
    for line in stdout.strip().split('\n'):
        line = line.strip()
        if line and not line.startswith('*') and not line.startswith('|') and not line.startswith('\\'):
            # 提取哈希值（前7位）和提交消息
            parts = line.split(' ', 1)
            if len(parts) >= 2:
                commit_hash = parts[0]
                message = parts[1]
                commits.append((commit_hash, message))
        elif line and (' ' in line):
            # 处理带 * 的行，如 "* abc1234 message"
            clean_line = line.replace('*', '').replace('|', '').strip()
            parts = clean_line.split(' ', 1)
            if len(parts) >= 2 and len(parts[0]) >= 7:
                commit_hash = parts[0]
                message = parts[1]
                commits.append((commit_hash, message))

    # 创建新窗口
    log_window = tk.Toplevel()
    log_window.title("提交历史")
    log_window.geometry("600x550")

    # 说明标签
    tk.Label(log_window, text="📋 提交历史列表",
             font=("Microsoft YaHei", 11, "bold"), pady=10).pack()

    tk.Label(log_window, text="选中一个版本，点击下方的【回退到选中版本】按钮",
             font=("Microsoft YaHei", 9), fg="gray").pack()

    # 创建列表框
    frame = tk.Frame(log_window)
    frame.pack(fill=tk.BOTH, expand=True, padx=10, pady=5)

    scrollbar = tk.Scrollbar(frame)
    scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

    listbox = tk.Listbox(frame, font=("Consolas", 10), yscrollcommand=scrollbar.set, selectmode=tk.SINGLE)
    listbox.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
    scrollbar.config(command=listbox.yview)

    # 填充列表
    for i, (commit_hash, message) in enumerate(commits):
        display_text = f"{commit_hash[:7]} - {message[:50]}"
        listbox.insert(tk.END, display_text)

    # 选中第一行（最新提交）
    if commits:
        listbox.selection_set(0)

    def do_reset():
        """执行回退操作"""
        selection = listbox.curselection()
        if not selection:
            messagebox.showwarning("提示", "请先选择一个版本")
            return

        index = selection[0]
        commit_hash, message = commits[index]

        # 确认对话框（二次确认）
        if not messagebox.askyesno("⚠️ 确认回退",
                                   f"确定要回退到以下版本？\n\n"
                                   f"提交哈希: {commit_hash[:7]}\n"
                                   f"提交消息: {message[:40]}...\n\n"
                                   f"⚠️ 警告：这将丢失当前未提交的更改！\n"
                                   f"⚠️ 如果已推送到远程，可能需要强制推送！"):
            return

        # 再次确认（三次确认，防止误操作）
        if not messagebox.askyesno("最终确认",
                                   f"最后确认：真的要回退到 {commit_hash[:7]} 吗？\n\n"
                                   f"此操作不可撤销！"):
            return

        # 执行回退
        success, _, stderr = run_git(f"git reset --hard {commit_hash}")
        if success:
            messagebox.showinfo("成功", f"✅ 已回退到版本: {commit_hash[:7]}")
            log_window.destroy()
            update_file_list()
            update_status_label()
        else:
            messagebox.showerror("错误", f"回退失败:\n{stderr}")

    # 按钮区域
    btn_frame = tk.Frame(log_window)
    btn_frame.pack(pady=10)

    tk.Button(btn_frame, text="⏪ 回退到选中版本", width=18, height=2,
              font=("Microsoft YaHei", 10), bg="#f44336", fg="white",
              command=do_reset).pack(side=tk.LEFT, padx=5)

    tk.Button(btn_frame, text="刷新", width=12, height=2,
              font=("Microsoft YaHei", 10),
              command=lambda: [log_window.destroy(), show_log()]).pack(side=tk.LEFT, padx=5)

    tk.Button(btn_frame, text="关闭", width=12, height=2,
              font=("Microsoft YaHei", 10),
              command=log_window.destroy).pack(side=tk.LEFT, padx=5)


def reset():
    """回退版本"""
    # 获取最近提交
    success, stdout, _ = run_git("git log --oneline -10")
    if not success:
        messagebox.showerror("错误", "无法获取提交历史")
        return
    
    # 显示提交历史并询问
    msg = "最近提交:\n\n" + stdout + "\n\n输入要回退的版本号 (前7位即可):"
    commit_hash = simpledialog.askstring("回退版本", msg)
    
    if not commit_hash:
        return
    
    if not messagebox.askyesno("确认", f"确定要回退到 {commit_hash}?\n这将丢失当前未提交的更改！"):
        return
    
    success, _, stderr = run_git(f"git reset --hard {commit_hash}")
    if success:
        # 强制推送到远程
        branch = get_branch()
        success_push, _, stderr_push = run_git(f"git push origin {branch} --force")
        if success_push:
            messagebox.showinfo("成功", "回退成功并已强制推送到 GitHub！")
        else:
            messagebox.showwarning("警告", f"本地回退成功，但推送失败:\n{stderr_push}")
        update_status_label()
    else:
        messagebox.showerror("错误", f"回退失败:\n{stderr}")


def update_file_list():
    """更新文件列表"""
    staged, modified, untracked = get_file_status()

    # 先启用编辑模式
    file_list_text.config(state=tk.NORMAL)
    file_list_text.delete(1.0, tk.END)

    if staged:
        file_list_text.insert(tk.END, "【已暂存 - 将被提交】\n", "staged_header")
        for f in staged:
            file_list_text.insert(tk.END, f"  ✓ {f}\n", "staged")
        file_list_text.insert(tk.END, "\n")

    if modified:
        file_list_text.insert(tk.END, "【已修改 - 未暂存】\n", "modified_header")
        for f in modified:
            file_list_text.insert(tk.END, f"  ⚡ {f}\n", "modified")
        file_list_text.insert(tk.END, "\n")

    if untracked:
        file_list_text.insert(tk.END, "【未跟踪 - 新文件】\n", "untracked_header")
        for f in untracked:
            file_list_text.insert(tk.END, f"  ? {f}\n", "untracked")
        file_list_text.insert(tk.END, "\n")

    if not staged and not modified and not untracked:
        file_list_text.insert(tk.END, "工作区干净，没有未提交的更改\n", "clean")

    # 写完后禁用编辑模式
    file_list_text.config(state=tk.DISABLED)


def update_status_label():
    """更新状态标签"""
    staged, modified, untracked = get_file_status()
    branch = get_branch()

    total = len(staged) + len(modified) + len(untracked)
    if total > 0:
        status_label.config(
            text=f"分支: {branch} | 已暂存: {len(staged)} | 已修改: {len(modified)} | 未跟踪: {len(untracked)}",
            fg="orange"
        )
    else:
        status_label.config(text=f"分支: {branch} | 工作区干净", fg="green")


def main():
    """主界面"""
    global status_label, file_list_text

    root = tk.Tk()
    root.title("Git 快捷工具 - lianghuaLearn")
    root.geometry("500x600")
    root.resizable(False, False)

    # 居中显示
    root.update_idletasks()
    x = (root.winfo_screenwidth() // 2) - 250
    y = (root.winfo_screenheight() // 2) - 300
    root.geometry(f'+{x}+{y}')

    # 标题
    tk.Label(root, text="🚀 Git 快捷工具", font=("Microsoft YaHei", 18, "bold"), pady=10).pack()
    tk.Label(root, text="项目: lianghuaLearn", font=("Microsoft YaHei", 10), fg="gray").pack()

    # 按钮框架
    btn_frame = tk.Frame(root)
    btn_frame.pack(pady=10)

    # 第一行按钮
    row1 = tk.Frame(btn_frame)
    row1.pack(pady=5)

    btn_width = 15
    btn_height = 2

    # 主要功能：提交并推送（一键完成）
    tk.Button(row1, text="📤 提交并推送", width=btn_width, height=btn_height,
              font=("Microsoft YaHei", 11, "bold"), bg="#2196F3", fg="white",
              command=commit_and_push).pack(side=tk.LEFT, padx=5)

    tk.Button(row1, text="📥 拉取最新代码", width=btn_width, height=btn_height,
              font=("Microsoft YaHei", 11), bg="#FF9800", fg="white",
              command=lambda: [pull(), update_file_list(), update_status_label()]).pack(side=tk.LEFT, padx=5)

    # 第二行按钮（辅助功能）
    row2 = tk.Frame(btn_frame)
    row2.pack(pady=5)

    tk.Button(row2, text="🔄 刷新状态", width=btn_width, height=btn_height,
              font=("Microsoft YaHei", 10),
              command=lambda: [update_file_list(), update_status_label()]).pack(side=tk.LEFT, padx=5)

    tk.Button(row2, text="📜 提交历史", width=btn_width, height=btn_height,
              font=("Microsoft YaHei", 10),
              command=show_log).pack(side=tk.LEFT, padx=5)

    # 文件列表显示区
    list_frame = tk.LabelFrame(root, text="文件状态", font=("Microsoft YaHei", 11), padx=10, pady=10)
    list_frame.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)

    file_list_text = scrolledtext.ScrolledText(list_frame, wrap=tk.WORD, font=("Consolas", 9), height=15)
    file_list_text.pack(fill=tk.BOTH, expand=True)

    # 配置文本样式
    file_list_text.tag_config("staged_header", foreground="#4CAF50", font=("Microsoft YaHei", 10, "bold"))
    file_list_text.tag_config("staged", foreground="#2E7D32")
    file_list_text.tag_config("modified_header", foreground="#FF9800", font=("Microsoft YaHei", 10, "bold"))
    file_list_text.tag_config("modified", foreground="#EF6C00")
    file_list_text.tag_config("untracked_header", foreground="#9E9E9E", font=("Microsoft YaHei", 10, "bold"))
    file_list_text.tag_config("untracked", foreground="#616161")
    file_list_text.tag_config("clean", foreground="#4CAF50", font=("Microsoft YaHei", 10))

    file_list_text.config(state=tk.DISABLED)

    # 状态显示
    status_frame = tk.Frame(root, bd=1, relief=tk.SUNKEN)
    status_frame.pack(side=tk.BOTTOM, fill=tk.X, padx=10, pady=10)

    status_label = tk.Label(status_frame, text="正在检查状态...", font=("Microsoft YaHei", 9))
    status_label.pack(pady=5)

    # 初始化状态
    update_file_list()
    update_status_label()

    root.mainloop()


if __name__ == "__main__":
    main()