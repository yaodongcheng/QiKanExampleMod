import requests
from bs4 import BeautifulSoup
import html2text
import time
import os
import re

# ================= 配置区域 =================
# 1. 替换为你的真实 Cookie (必须是付费账号登录后的 Cookie)
COOKIE = 'YOUR_COOKIE_HERE' 

# 2. 专栏目录 URL
CATEGORY_URL = "https://blog.csdn.net/qq_35829452/category_12538930.html?spm=1001.2014.3001.5482&orderBy=2"

# 3. 输出文件夹名称
OUTPUT_DIR = "CSDN_Column_Export"
# ===========================================

# 设置请求头，伪装成浏览器
HEADERS = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Cookie': COOKIE,
    'Referer': 'https://blog.csdn.net/'
}

def get_article_links(category_url):
    """从专栏目录页获取所有文章链接"""
    print(f"正在获取文章列表: {category_url}")
    try:
        resp = requests.get(category_url, headers=HEADERS)
        resp.raise_for_status()
        soup = BeautifulSoup(resp.text, 'html.parser')
        
        links = []
        # CSDN 专栏列表通常在 ul.column_article_list li a 或者类似结构
        # 针对该特定专栏页面的解析逻辑
        article_list = soup.find_all('ul', class_='column_article_list')
        
        if not article_list:
            # 备用解析策略，寻找一般的文章列表结构
            print("尝试备用解析策略...")
            items = soup.find_all('a', href=True)
            for item in items:
                href = item['href']
                if 'article/details' in href and 'qq_35829452' in href:
                    if href not in links:
                        links.append(href)
        else:
            for ul in article_list:
                items = ul.find_all('a', href=True)
                for item in items:
                    href = item['href']
                    # 过滤掉非文章链接
                    if 'article/details' in href:
                        links.append(href)
        
        # 去重并保持顺序（如果是orderBy=2，通常是正序，但也可能倒序，这里只管抓取）
        unique_links = []
        [unique_links.append(i) for i in links if i not in unique_links]
        
        print(f"共找到 {len(unique_links)} 篇文章。")
        return unique_links

    except Exception as e:
        print(f"获取文章列表失败: {e}")
        return []

def download_article(url, index):
    """下载单篇文章并转换为 Markdown"""
    try:
        print(f"正在下载第 {index} 篇: {url}")
        resp = requests.get(url, headers=HEADERS)
        soup = BeautifulSoup(resp.text, 'html.parser')
        
        # 获取标题
        title_tag = soup.find('h1', id='articleContentId')
        title = title_tag.get_text().strip() if title_tag else f"Article_{index}"
        
        # 清理文件名非法字符
        safe_title = re.sub(r'[\\/*?:"<>|]', "", title)
        filename = f"{index:02d}_{safe_title}.md"
        
        # 获取正文内容 (通常在 id="content_views")
        content_div = soup.find('div', id='content_views')
        
        if not content_div:
            print(f"警告：无法找到文章正文内容，可能是 Cookie 失效或反爬。跳过: {title}")
            return None, None

        # 处理图片懒加载 (CSDN 图片 url 经常放在 data-src 中)
        for img in content_div.find_all('img'):
            if 'src' not in img.attrs and 'data-src' in img.attrs:
                img['src'] = img['data-src']
        
        # 将 HTML 转换为 Markdown
        converter = html2text.HTML2Text()
        converter.ignore_links = False
        converter.ignore_images = False
        converter.body_width = 0 # 不自动换行
        markdown_content = converter.handle(str(content_div))
        
        # 添加标题到 Markdown 头部
        final_content = f"# {title}\n\n链接: {url}\n\n{markdown_content}"
        
        return filename, final_content
        
    except Exception as e:
        print(f"下载文章失败 {url}: {e}")
        return None, None

def main():
    if not os.path.exists(OUTPUT_DIR):
        os.makedirs(OUTPUT_DIR)
        
    links = get_article_links(CATEGORY_URL)
    
    if not links:
        print("未找到文章，请检查 URL 或 Cookie。")
        return

    # 为了整合文档，我们按顺序反转列表（如果网页是倒序排列的话）
    # 或者直接按网页顺序下载。如果不确定顺序，建议先运行一次看文件名序号。
    # 这里假设网页是从第1篇到第29篇显示，或者你需要自己根据实际情况调整 `links[::-1]`
    
    merged_content = f"# CSDN 专栏导出汇总\n\n来源: {CATEGORY_URL}\n导出时间: {time.strftime('%Y-%m-%d')}\n\n---\n\n"
    
    for i, link in enumerate(links, 1):
        filename, content = download_article(link, i)
        
        if filename and content:
            # 1. 保存单个文件
            file_path = os.path.join(OUTPUT_DIR, filename)
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(content)
            
            # 2. 追加到合并内容
            merged_content += content + "\n\n---\n\n" # 添加分页符
            
            # 礼貌性延时，防止触发反爬虫
            time.sleep(1.5)
            
    # 保存合并后的大文件
    merged_file_path = os.path.join(OUTPUT_DIR, "ALL_MERGED_ARTICLES.md")
    with open(merged_file_path, 'w', encoding='utf-8') as f:
        f.write(merged_content)
        
    print(f"\n全部完成！\n单篇文件保存在: {OUTPUT_DIR}\n整合文件保存在: {merged_file_path}")

if __name__ == "__main__":
    main()