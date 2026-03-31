#include <windows.h>
#include <graphics.h>
#include <conio.h>
#include <time.h>
#include <math.h>
#include <stdlib.h>

// 点结构体：存储坐标与颜色信息
struct Point {
    double x, y;
    COLORREF color;
};

// 预设粉红色系颜色数组
COLORREF colors[256] = { RGB(255, 32, 83), RGB(252, 222, 250), RGB(255, 0, 0),
                         RGB(255, 0, 0), RGB(255, 2, 2), RGB(255, 0, 8), RGB(255, 5, 5) };

// 屏幕尺寸（全局变量）
int xScreen = GetSystemMetrics(SM_CXSCREEN);
int yScreen = GetSystemMetrics(SM_CYSCREEN);

// 核心数学常数
const double PI = 3.1415926535;
const double e = 2.71828;

// 粒子系统核心参数
const double average_distance = 0.162;  // 粒子平均间距
const int quantity = 506;               // 基础粒子数量
const int circles = 210;                // 扩散圈数
const int frames = 20;                  // 动画帧数

// 全局数据存储
Point origin_points[quantity];          // 原始心形点集
Point points[circles * quantity];       // 动态扩散粒子点集
IMAGE images[frames];                   // 动画帧缓冲区

// 坐标系转换：数学坐标 -> 屏幕坐标（原点移至屏幕中心，Y轴翻转）
double screen_x(double x) {
    return x + xScreen / 2;
}

double screen_y(double y) {
    return -y + yScreen / 2;
}

// 生成[x1, x2]范围内的随机整数
int create_random(int x1, int x2) {
    if (x2 > x1)
        return rand() % (x2 - x1 + 1) + x1;
    return x1;
}

// 创建粒子数据与所有动画帧（核心初始化逻辑）
void create_data() {
    // 生成原始心形点集（笛卡尔心形参数方程）
    int index = 0;
    double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
    for (double radian = 0.1; radian <= 2 * PI; radian += 0.005) {
        x2 = 16 * pow(sin(radian), 3);
        y2 = 13 * cos(radian) - 5 * cos(2 * radian) - 2 * cos(3 * radian) - cos(4 * radian);

        // 按间距筛选点，保证点集分布均匀
        double distance = sqrt(pow(x2 - x1, 2) + pow(y2 - y1, 2));
        if (distance > average_distance) {
            x1 = x2, y1 = y2;
            origin_points[index].x = x2;
            origin_points[index++].y = y2;
        }
    }

    // 生成多层扩散的动态粒子（带亮度渐变与概率衰减）
    index = 0;
    for (double size = 0.1, lightness = 1.5; size <= 20; size += 0.1) {
        double success_p = 1 / (1 + pow(e, 8 - size / 2)); // 粒子生成概率衰减曲线
        if (lightness > 1) lightness -= 0.0025;            // 颜色亮度渐变降低

        for (int i = 0; i < quantity; ++i) {
            if (success_p > create_random(0, 100) / 100.0) {
                COLORREF color = colors[create_random(0, 6)];
                // 调整粒子颜色亮度
                points[index].color = RGB(
                    GetRValue(color) / lightness,
                    GetGValue(color) / lightness,
                    GetBValue(color) / lightness
                );
                // 粒子位置：按比例放大+随机小扰动
                points[index].x = size * origin_points[i].x + create_random(-4, 4);
                points[index++].y = size * origin_points[i].y + create_random(-4, 4);
            }
        }
    }

    int points_size = index;

    // 预生成所有动画帧
    for (int frame = 0; frame < frames; ++frame) {
        images[frame] = IMAGE(xScreen, yScreen);
        SetWorkingImage(&images[frame]);
        clearcliprgn();

        // 设置黑色为透明背景并清空画面
        setbkcolor(BLACK);
        cleardevice();
        setbkmode(TRANSPARENT);

        // 更新粒子位置并绘制（抛物线运动轨迹）
        for (index = 0; index < points_size; ++index) {
            double x = points[index].x, y = points[index].y;
            double distance = sqrt(x * x + y * y);
            double distance_increase = -0.0009 * distance * distance + 0.35714 * distance + 5;
            double x_increase = distance_increase * x / distance / frames;
            double y_increase = distance_increase * y / distance / frames;

            // 更新粒子坐标
            points[index].x += x_increase;
            points[index].y += y_increase;

            // 绘制实心粒子
            setfillcolor(points[index].color);
            solidcircle((int)screen_x(points[index].x), (int)screen_y(points[index].y), 1);
        }

        // 添加随机闪烁粒子（增强视觉效果）
        for (double size = 17; size < 23; size += 0.3) {
            for (index = 0; index < quantity; ++index) {
                if ((create_random(0, 100) / 100.0 > 0.6 && size >= 20) ||
                    (size < 20 && create_random(0, 100) / 100.0 > 0.95))
                {
                    double x, y;
                    // 大尺寸粒子添加更大幅度扰动
                    if (size >= 20) {
                        x = origin_points[index].x * size + create_random(-frame * frame / 5 - 15, frame * frame / 5 + 15);
                        y = origin_points[index].y * size + create_random(-frame * frame / 5 - 15, frame * frame / 5 + 15);
                    }
                    else {
                        x = origin_points[index].x * size + create_random(-5, 5);
                        y = origin_points[index].y * size + create_random(-5, 5);
                    }
                    setfillcolor(colors[create_random(0, 6)]);
                    solidcircle((int)screen_x(x), (int)screen_y(y), 1);
                }
            }
        }
        SetWorkingImage(); // 恢复默认绘图目标
    }
}

// 隐藏控制台窗口
void HideConsole() {
    HWND hwnd = GetConsoleWindow();
    ShowWindow(hwnd, SW_HIDE);
}

// 设置分层窗口透明度（alpha: 0-255, 0完全透明, 255完全不透明）
void SetWindowTransparency(HWND hwnd, BYTE alpha) {
    SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
    SetLayeredWindowAttributes(hwnd, RGB(0, 0, 0), alpha, LWA_ALPHA | LWA_COLORKEY);
}

// 创建透明无边框全屏顶层窗口
HWND CreateTransparentWindow() {
    HWND hwnd = initgraph(xScreen, yScreen, EW_SHOWCONSOLE | EW_NOCLOSE | EW_NOMINIMIZE);
    // 移除窗口边框与标题栏
    SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_CAPTION);
    // 设置窗口置顶
    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, xScreen, yScreen, SWP_SHOWWINDOW);
    // 设置窗口透明度
    SetWindowTransparency(hwnd, 220);
    return hwnd;
}

int main() {
    // 隐藏控制台窗口
    HideConsole();

    // 创建透明全屏窗口
    HWND hwnd = CreateTransparentWindow();

    // 初始化绘图环境
    setbkcolor(BLACK);
    cleardevice();
    setbkmode(TRANSPARENT);
    BeginBatchDraw();

    // 初始化随机数种子
    srand((unsigned int)time(NULL));

    // 预生成粒子数据与动画帧
    create_data();

    // 动画循环：呼吸式播放（先扩展后收缩）
    bool extend = true;
    int frame = 0;
    MSG msg = { 0 };

    while (true) {
        // 消息循环：处理窗口退出消息
        while (PeekMessage(&msg, NULL, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
            if (msg.message == WM_QUIT) {
                EndBatchDraw();
                closegraph();
                return 0;
            }
        }

        // ESC键退出程序
        if (GetAsyncKeyState(VK_ESCAPE) & 0x8000) {
            break;
        }

        // 绘制当前帧并刷新
        putimage(0, 0, &images[frame]);
        FlushBatchDraw();

        // 控制动画帧率
        Sleep(20);

        // 清空当前画面
        cleardevice();

        // 更新帧索引（呼吸效果：先递增到最后一帧，再递减到第一帧）
        if (extend) {
            if (frame == frames - 1) {
                extend = false;
            }
            else {
                ++frame;
            }
        }
        else {
            if (frame == 0) {
                extend = true;
            }
            else {
                --frame;
            }
        }
    }

    // 释放资源
    EndBatchDraw();
    closegraph();

    return 0;
}