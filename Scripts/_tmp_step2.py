# -*- coding: utf-8 -*-
import io

p = 'GUI/Prefabs/ImChatCompact.xml'
t = io.open(p, encoding='utf-8').read()

start = t.index('<!-- ═══ 输入行：频道下拉')  # ═══ 输入行：频道下拉
# 输入行结束：body </Children> 前的最后一个 </Widget>
end_marker = '\n              </Children>\n            </ListPanel>\n'
end = t.index(end_marker)
tail = t[:end]
last_close = tail.rindex('\n                </Widget>')
end = last_close + len('\n                </Widget>')

new_input_row = '''                <!-- ═══ 输入行：输入框 + 发送（2026-08-15 用户裁定：频道切换在标题行，输入行独占最宽）═══ -->
                <Widget WidthSizePolicy="StretchToParent" HeightSizePolicy="Fixed" SuggestedHeight="50"
                        Sprite="BlankWhiteSquare_9" Color="#00000088"
                        DoNotAcceptEvents="true">
                  <Children>
                    <!-- 分隔线：输入行与消息区边界 -->
                    <Widget WidthSizePolicy="StretchToParent" HeightSizePolicy="Fixed" SuggestedHeight="2"
                            VerticalAlignment="Top"
                            Sprite="BlankWhiteSquare_9" Color="#FFFFFF33"
                            DoNotAcceptEvents="true"/>
                    <ListPanel WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent"
                               StackLayout.LayoutMethod="HorizontalLeftToRight"
                               DoNotAcceptEvents="true">
                      <Children>
                        <!-- 输入框（与完整模式同字段：切模式草稿保留） -->
                        <EditableTextWidget Id="LWN_ImChat_CompactInput" WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent"
                                            Text="@InputText" DefaultSearchText="@PlaceholderText"
                                            TextColor="#FFFFFFFF" Brush="MyBrush_16_Left" Brush.FontSize="16"
                                            MarginRight="6" VerticalAlignment="Center"/>
                        <!-- 发送按钮 -->
                        <ButtonWidget WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="64" SuggestedHeight="34"
                                      Brush="Test.Button3"
                                      Command.Click="ExecuteSend"
                                      IsEnabled="@CanSend"
                                      VerticalAlignment="Center" MarginRight="8">
                          <Children>
                            <TextWidget WidthSizePolicy="CoverChildren" HeightSizePolicy="CoverChildren"
                                        Text="@SendText" Brush="MyBrush_18_White" Brush.FontSize="14"
                                        HorizontalAlignment="Center" VerticalAlignment="Center"
                                        DoNotAcceptEvents="true"/>
                          </Children>
                        </ButtonWidget>
                      </Children>
                    </ListPanel>
                  </Children>
                </Widget>
'''
t = t[:start] + new_input_row + t[end:]
io.open(p, 'w', encoding='utf-8').write(t)
print('input row replaced, len', len(t))
