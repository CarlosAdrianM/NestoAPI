using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Infraestructure.Informes
{
    public class InformeClientesNuevosJobsService
    {
        private const string EMPRESA = Empresas.EMPRESA_POR_DEFECTO;
        private const string CC_FIJO = "manuelrodriguez@nuevavision.es";
        private const string BCC_FIJO = "carlosadrian@nuevavision.es";

        public static async Task ProcesarInformeSemanal()
        {
            try
            {
                // Redondear a las 9:00 del viernes para evitar huecos/duplicados
                // independientemente del segundo exacto en que Hangfire ejecute el job
                DateTime hoy = DateTime.Today; // 00:00 de hoy
                DateTime fechaHasta = hoy.AddHours(9); // viernes actual 9:00
                DateTime fechaDesde = fechaHasta.AddDays(-7); // viernes anterior 9:00

                var clientesPorVendedor = await ObtenerClientesNuevosPorVendedor(fechaDesde, fechaHasta)
                    .ConfigureAwait(false);

                if (!clientesPorVendedor.Any())
                {
                    return;
                }

                foreach (var grupo in clientesPorVendedor)
                {
                    try
                    {
                        await EnviarCorreoVendedor(grupo.Key, grupo.Value).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Continuar con los demás vendedores
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        internal static async Task<Dictionary<string, List<ClienteNuevoVendedorDTO>>> ObtenerClientesNuevosPorVendedor(
            DateTime fechaDesde, DateTime fechaHasta)
        {
            var resultado = new Dictionary<string, List<ClienteNuevoVendedorDTO>>();

            using (NVEntities db = new NVEntities())
            {
                // Clientes modificados en la ventana temporal
                var clientesNuevos = await db.Clientes
                    .Where(c => c.Empresa == EMPRESA
                        && c.Fecha_Modificación >= fechaDesde
                        && c.Fecha_Modificación < fechaHasta)
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (!clientesNuevos.Any())
                {
                    return resultado;
                }

                var clienteIds = clientesNuevos.Select(c => c.Nº_Cliente).Distinct().ToList();

                // VendedoresClienteGrupoProducto para esos clientes
                var vcgps = await db.VendedoresClientesGruposProductos
                    .Where(v => v.Empresa == EMPRESA && clienteIds.Contains(v.Cliente))
                    .ToListAsync()
                    .ConfigureAwait(false);

                foreach (var cliente in clientesNuevos)
                {
                    string vendedorCliente = cliente.Vendedor?.Trim();

                    // Fuente 1: Vendedor de la tabla Clientes
                    if (!string.IsNullOrWhiteSpace(vendedorCliente))
                    {
                        AgregarClienteAVendedor(resultado, vendedorCliente, cliente, cliente.Estado ?? 0, "Clientes");
                    }

                    // Fuente 2: Vendedores de VendedoresClienteGrupoProducto
                    var vcgpsCliente = vcgps
                        .Where(v => v.Cliente.Trim() == cliente.Nº_Cliente.Trim()
                            && v.Contacto.Trim() == cliente.Contacto.Trim())
                        .ToList();

                    var vendedoresVcgp = vcgpsCliente
                        .Select(v => new { Vendedor = v.Vendedor?.Trim(), v.Estado })
                        .Where(v => !string.IsNullOrWhiteSpace(v.Vendedor))
                        .GroupBy(v => v.Vendedor)
                        .ToList();

                    foreach (var grupoVcgp in vendedoresVcgp)
                    {
                        // Evitar duplicar si coincide con el vendedor de Clientes
                        if (grupoVcgp.Key == vendedorCliente)
                        {
                            continue;
                        }

                        short estadoVcgp = grupoVcgp.First().Estado;
                        AgregarClienteAVendedor(resultado, grupoVcgp.Key, cliente, estadoVcgp, "VendedoresClienteGrupoProducto");
                    }
                }
            }

            // Ordenar cada lista por código postal y dirección
            foreach (var key in resultado.Keys.ToList())
            {
                resultado[key] = resultado[key]
                    .OrderBy(c => c.CodigoPostal)
                    .ThenBy(c => c.Direccion)
                    .ToList();
            }

            return resultado;
        }

        private static void AgregarClienteAVendedor(
            Dictionary<string, List<ClienteNuevoVendedorDTO>> resultado,
            string vendedor,
            Cliente cliente,
            short estado,
            string origen)
        {
            if (!resultado.ContainsKey(vendedor))
            {
                resultado[vendedor] = new List<ClienteNuevoVendedorDTO>();
            }

            resultado[vendedor].Add(new ClienteNuevoVendedorDTO
            {
                Cliente = cliente.Nº_Cliente?.Trim(),
                Contacto = cliente.Contacto?.Trim(),
                Nombre = cliente.Nombre?.Trim(),
                Direccion = cliente.Dirección?.Trim(),
                CodigoPostal = cliente.CodPostal?.Trim(),
                Telefono = cliente.Teléfono?.Trim(),
                Estado = estado,
                Origen = origen
            });
        }

        internal static async Task EnviarCorreoVendedor(string vendedorCodigo, List<ClienteNuevoVendedorDTO> clientes)
        {
            using (NVEntities db = new NVEntities())
            {
                var vendedor = await db.Vendedores
                    .FirstOrDefaultAsync(v => v.Empresa == EMPRESA && v.Número == vendedorCodigo)
                    .ConfigureAwait(false);

                if (vendedor == null || string.IsNullOrWhiteSpace(vendedor.Mail))
                {
                    return;
                }

                string correoVendedor = vendedor.Mail.Trim();
                string nombreVendedor = vendedor.Descripción?.Trim() ?? vendedorCodigo;

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión");
                    mail.To.Add(correoVendedor);
                    mail.CC.Add(CC_FIJO);
                    mail.Bcc.Add(BCC_FIJO);

                    // Buscar superior para CC
                    var equipo = await db.EquiposVentas
                        .FirstOrDefaultAsync(e => e.Empresa == EMPRESA
                            && e.Vendedor == vendedorCodigo
                            && (e.FechaHasta == null || e.FechaHasta >= DateTime.Now))
                        .ConfigureAwait(false);

                    if (equipo != null && !string.IsNullOrWhiteSpace(equipo.Superior))
                    {
                        string superiorCodigo = equipo.Superior;
                        var superior = await db.Vendedores
                            .FirstOrDefaultAsync(v => v.Empresa == EMPRESA && v.Número == superiorCodigo)
                            .ConfigureAwait(false);

                        if (superior != null && !string.IsNullOrWhiteSpace(superior.Mail))
                        {
                            mail.CC.Add(superior.Mail.Trim());
                        }
                    }

                    mail.Subject = $"Clientes nuevos de la semana - {nombreVendedor} ({clientes.Count})";
                    mail.Body = GenerarHtml(nombreVendedor, clientes);
                    mail.IsBodyHtml = true;

                    var servicioCorreo = new ServicioCorreoElectronico();
                    servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
        }

        internal static string GenerarHtml(string nombreVendedor, List<ClienteNuevoVendedorDTO> clientes)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif;'>");
            sb.AppendLine($"<h2>Clientes nuevos de la semana</h2>");
            sb.AppendLine($"<p>Hola {nombreVendedor}, estos son los clientes nuevos asignados a ti esta semana:</p>");

            sb.AppendLine("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse: collapse; width: 100%; font-size: 13px;'>");
            sb.AppendLine("<tr style='background-color: #4A90D9; color: white;'>");
            sb.AppendLine("<th>Cliente</th><th>Contacto</th><th>Nombre</th><th>Dirección</th><th>C.P.</th><th>Teléfono</th><th>Estado</th>");
            sb.AppendLine("</tr>");

            bool alternar = false;
            foreach (var c in clientes)
            {
                string bgColor = alternar ? "#f2f2f2" : "#ffffff";
                sb.AppendLine($"<tr style='background-color: {bgColor};'>");
                sb.AppendLine($"<td>{c.Cliente}</td>");
                sb.AppendLine($"<td>{c.Contacto}</td>");
                sb.AppendLine($"<td>{c.Nombre}</td>");
                sb.AppendLine($"<td>{c.Direccion}</td>");
                sb.AppendLine($"<td>{c.CodigoPostal}</td>");
                sb.AppendLine($"<td>{c.Telefono}</td>");
                sb.AppendLine($"<td style='text-align: center;'>{c.Estado}</td>");
                sb.AppendLine("</tr>");
                alternar = !alternar;
            }

            sb.AppendLine("</table>");
            sb.AppendLine($"<p style='color: #888; font-size: 11px; margin-top: 20px;'>Informe generado el {DateTime.Now:dd/MM/yyyy HH:mm}</p>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }
    }
}
