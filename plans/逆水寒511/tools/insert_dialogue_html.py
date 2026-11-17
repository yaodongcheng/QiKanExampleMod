"""旧对白插入命令的兼容入口；现在统一生成 DesignData 可视化页面。

页面是生成物，修改 gen_story_preview.py 或 story_preview.template.html 后重生。
"""
from gen_story_preview import main


if __name__ == "__main__":
    main()
