# 骑砍Ⅱ霸主MOD开发(18)-存档

> 来源: https://blog.csdn.net/qq_35829452/article/details/141031492
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.存档文件路径

     C:\Users\taohu\Documents\Mount and Blade II Bannerlord\Game Saves\save001.sav

二.类字段存档实现方法

     <1.声明MySaveContextTypeDefiner:

    public class MySaveContextTypeDefiner : SaveableTypeDefiner
    {
        public MySaveContextTypeDefiner() : base(980000)
        {
        }
        protected override void DefineClassTypes()
        {
           AddClassDefinition(typeof(Student), 1, null);
        }
    }

     <2.声明存储对象对应的字段:

 public class Student {

    [SaveableProperty(3)]
    public int Age;  

    [SaveableProperty(3)]
    public int Height;       
}

三.存档读取和写入

#读取
SaveManager.Load()
#写入
SaveManager.Save()

                
