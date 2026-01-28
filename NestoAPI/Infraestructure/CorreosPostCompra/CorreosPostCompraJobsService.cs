using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// Servicio con métodos estáticos para jobs de Hangfire de correos post-compra.
    /// Issue #74: Sistema de correos automáticos con videos personalizados post-compra.
    /// </summary>
    public class CorreosPostCompraJobsService
    {
        /// <summary>
        /// Job que se ejecuta diariamente después de las 20h.
        /// Obtiene todos los albaranes del día y programa el envío de correos para dentro de 3 días.
        /// </summary>
        public static async Task ProcesarAlbaranesDiarios()
        {
            Console.WriteLine("🚀 [Hangfire] Iniciando procesamiento de albaranes para correos post-compra...");

            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;

                    // Obtener los números de pedido con albaranes de hoy
                    DateTime hoy = DateTime.Today;

                    var pedidosConAlbaran = await db.LinPedidoVtas
                        .Where(l => l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                                    DbFunctions.TruncateTime(l.Fecha_Albarán) == hoy &&
                                    l.TipoLinea == 1 &&
                                    l.Cantidad >= 1)
                        .Select(l => new { l.Empresa, l.Número })
                        .Distinct()
                        .ToListAsync()
                        .ConfigureAwait(false);

                    Console.WriteLine($"📦 [Hangfire] Encontrados {pedidosConAlbaran.Count} pedidos con albarán hoy");

                    if (!pedidosConAlbaran.Any())
                    {
                        Console.WriteLine("✅ [Hangfire] No hay albaranes que procesar hoy");
                        return;
                    }

                    // Programar el envío para dentro de 3 días
                    DateTime fechaEnvio = DateTime.Now.AddDays(3).Date.AddHours(10); // A las 10:00 de dentro de 3 días

                    foreach (var pedido in pedidosConAlbaran)
                    {
                        try
                        {
                            // Programar el job de envío de correo para dentro de 3 días
                            Hangfire.BackgroundJob.Schedule(
                                () => EnviarCorreoPostCompra(pedido.Empresa, pedido.Número),
                                fechaEnvio
                            );

                            Console.WriteLine($"📧 [Hangfire] Programado correo para pedido {pedido.Número} - Fecha envío: {fechaEnvio}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ [Hangfire] Error programando correo para pedido {pedido.Número}: {ex.Message}");
                            // Continuar con los demás pedidos
                        }
                    }

                    Console.WriteLine($"✅ [Hangfire] Procesamiento completado. {pedidosConAlbaran.Count} correos programados para {fechaEnvio}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Hangfire] Error en procesamiento de albaranes: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }

        /// <summary>
        /// Job que envía el correo post-compra para un pedido específico.
        /// Se ejecuta 3 días después de crear el albarán.
        /// </summary>
        public static async Task EnviarCorreoPostCompra(string empresa, int numeroPedido)
        {
            Console.WriteLine($"📧 [Hangfire] Enviando correo post-compra para pedido {empresa}/{numeroPedido}...");

            try
            {
                var servicioRecomendaciones = new ServicioRecomendacionesPostCompra();

                // Obtener recomendaciones
                var recomendaciones = await servicioRecomendaciones
                    .ObtenerRecomendaciones(empresa, numeroPedido)
                    .ConfigureAwait(false);

                if (recomendaciones == null)
                {
                    Console.WriteLine($"⚠️ [Hangfire] Pedido {numeroPedido} no encontrado");
                    return;
                }

                if (string.IsNullOrWhiteSpace(recomendaciones.ClienteEmail))
                {
                    Console.WriteLine($"⚠️ [Hangfire] Cliente {recomendaciones.ClienteId} sin email. No se envía correo.");
                    return;
                }

                if (recomendaciones.Videos == null || recomendaciones.Videos.Count == 0)
                {
                    Console.WriteLine($"⚠️ [Hangfire] Pedido {numeroPedido} sin videos relacionados. No se envía correo.");
                    return;
                }

                // Generar contenido del correo
                var generador = new GeneradorContenidoCorreoPostCompra(
                    new OpenAI.ServicioOpenAI()
                );

                string htmlCorreo = await generador
                    .GenerarContenidoHtml(recomendaciones)
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(htmlCorreo))
                {
                    Console.WriteLine($"⚠️ [Hangfire] Error generando contenido HTML para pedido {numeroPedido}");
                    return;
                }

                // TODO: Enviar correo con Mailchimp
                // Por ahora solo logueamos que lo enviaríamos
                Console.WriteLine($"📧 [Hangfire] Correo generado para {recomendaciones.ClienteEmail}:");
                Console.WriteLine($"   - Cliente: {recomendaciones.ClienteNombre}");
                Console.WriteLine($"   - Videos incluidos: {recomendaciones.Videos.Count}");
                Console.WriteLine($"   - HTML generado: {htmlCorreo.Length} caracteres");

                // Aquí irá la integración con Mailchimp:
                // var servicioMailchimp = new ServicioMailchimp();
                // await servicioMailchimp.EnviarCorreo(
                //     recomendaciones.ClienteEmail,
                //     $"Saca el máximo partido a tu compra, {recomendaciones.ClienteNombre}",
                //     htmlCorreo
                // );

                Console.WriteLine($"✅ [Hangfire] Correo procesado para pedido {numeroPedido}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Hangfire] Error enviando correo para pedido {numeroPedido}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }
    }
}
