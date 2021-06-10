using NestoAPI.Models;
using NestoAPI.Models.Depositos;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Depositos
{
    public class ServicioDeposito : IServicioDeposito
    {
        private readonly DateTime fechaDesde = DateTime.Today.AddDays(-Constantes.Productos.DEPOSITO_DIAS_ESTADISTICA);
        private readonly NVEntities db = new NVEntities();
        public async Task<bool> EnviarCorreoSMTP(MailMessage mail)
        {
            using (SmtpClient client = new SmtpClient())
            {                
                client.Port = 587;
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.UseDefaultCredentials = false;
                string contrasenna = ConfigurationManager.AppSettings["office365password"];
                client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
                client.Host = "smtp.office365.com";
                try
                {
                    await client.SendMailAsync(mail).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    /*
                    await Task.Delay(2000);
                    client.Send(mail);
                    */
                    return false;
                }                
            }
                
            return true;
        }

        public async Task<DateTime> LeerFechaPrimerVencimiento(string producto)
        {
            return await db.ExtractosProducto.Where(e => e.Número == producto).Select(e => e.Fecha).DefaultIfEmpty(DateTime.MinValue).MinAsync().ConfigureAwait(false);
        }

        public Task<List<ProductoDTO>> LeerProductosProveedor(string proveedorId)
        {
            // Leer de linpedidocm
            IQueryable<ProductoDTO> productos = db.Productos.Include(nameof(ClasificacionMasVendido)).Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Estado >= Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
                .Join(db.LinPedidoCmps.Where(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN && l.NºProveedor == proveedorId), 
                    p => new { producto = p.Número }, 
                    v => new { producto = v.Producto }, 
                    (p, v) => new { Producto = v.Producto, p.Nombre, p.Estado, p.ClasificacionMasVendido })
                .GroupBy(g => new { g.Producto, g.Nombre, g.Estado, g.ClasificacionMasVendido })
                .Select(x => new ProductoDTO {
                    Producto = x.Key.Producto.Trim(),
                    Nombre = x.Key.Nombre.Trim(),
                    Estado = (short)x.Key.Estado,
                    ClasificacionMasVendidos = x.Key.ClasificacionMasVendido.Posicion
                });
            return productos.ToListAsync();
        }

        public async Task<List<PersonaContactoProveedorDTO>> LeerProveedoresEnDeposito()
        {
            return await db.PersonasContactoProveedores
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Cargo == Constantes.Proveedores.PersonasContacto.INFORMACION_PRODUCTO_DEPOSITO)
                .Join(db.Proveedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO),
                    p => new { prov = p.NºProveedor},
                    v => new {prov = v.Número},
                    (p,v) => new {p.CorreoElectrónico, NombrePersonaContacto = p.Nombre, p.NºProveedor, NombreProvedor =  v.Nombre}
                )
                .Select(p => new PersonaContactoProveedorDTO
                {
                    CorreoElectronico = p.CorreoElectrónico.Trim(),
                    NombrePersonaContacto = p.NombrePersonaContacto.Trim(),
                    NombreProveedor = p.NombreProvedor.Trim(),
                    ProveedorId = p.NºProveedor
                }).ToListAsync().ConfigureAwait(false);
        }

        public Task<int> LeerUnidadesDevueltas(string producto)
        {
            return db.LinPedidoVtas
                .Where(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN && l.Producto == producto && l.Fecha_Albarán >= fechaDesde && l.Cantidad < 0)
                .Select(l => -(int)l.Cantidad)
                .DefaultIfEmpty(0)
                .SumAsync();
        }

        public Task<int> LeerUnidadesEnviadasProveedor(string producto)
        {
            return db.LinPedidoCmps
                .Where(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN && l.Producto == producto)
                .Select(l => (int)l.Cantidad)
                .DefaultIfEmpty(0)
                .SumAsync();
        }

        public Task<int> LeerUnidadesVendidas(string producto)
        {
            return db.LinPedidoVtas
                .Where(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN && l.Producto == producto && l.Fecha_Albarán >= fechaDesde && l.Cantidad > 0)
                .Select(l => (int)(l.Cantidad-l.Recoger))
                .DefaultIfEmpty(0)
                .SumAsync();
        }
    }
}