using AutoScenarioX.Api.Common.Options;
using AutoScenarioX.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ---- Đăng ký dịch vụ theo 3 lớp ----
builder.Services
    .AddPresentation(builder.Configuration)       // Controllers, lỗi, CORS, Swagger
    .AddApiAuthentication(builder.Configuration, builder.Environment)  // Xác thực (Dev hoặc Supabase JWT) + phân quyền
    .AddBusinessServices(builder.Configuration)   // Services
    .AddDataAccess(builder.Configuration);        // DbContext, Repositories, UnitOfWork

var app = builder.Build();

// ---- Pipeline xử lý request (thứ tự quan trọng) ----
app.UseExceptionHandler();                        // bắt mọi exception -> ProblemDetails
app.UseStatusCodePages();                         // 404 route không tồn tại cũng trả ProblemDetails

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();                             // /swagger/v1/swagger.json
    app.UseSwaggerUI();                           // /swagger - giao diện thử API
}

// Development không ép https: profile https của Rider mở cả 5080 và 7080, nếu 5080 trả 307
// thì proxy /api của Vite bị đẩy sang origin khác và trình duyệt chặn CORS. Ngoài Development
// vẫn ép https như cũ.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseCors(FrontendCorsOptions.PolicyName);
app.UseAuthentication();                          // đọc token -> ClaimsPrincipal
app.UseAuthorization();                           // áp policy / [Authorize]
app.MapControllers();

app.Run();

public partial class Program { }
