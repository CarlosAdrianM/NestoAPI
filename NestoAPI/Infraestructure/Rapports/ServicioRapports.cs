using NestoAPI.Models;
using NestoAPI.Models.Rapports;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Rapports
{
    public class ServicioRapports: IServicioRapports
    {
        public async Task<ICollection<CodigoPostalSeguimientoLookup>> CodigosPostalesSinVisitar(string vendedor, DateTime fechaDesde, DateTime fechaHasta)
        {
            
            using (NVEntities db = new NVEntities())
            {
                IQueryable<string> listaPorGrupo = db.VendedoresCodigoPostalGruposProductos.Where(g => g.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && g.Vendedor == vendedor).Select(g => g.CodigoPostal);
                IQueryable<string> listaVisitados = db.SeguimientosClientes
                    .Where(s => s.Vendedor == vendedor && s.Tipo == Constantes.SeguimientosCliente.Tipos.TIPO_VISITA_PRESENCIAL && s.Fecha >= fechaDesde && s.Fecha <= fechaHasta)
                    .Join(db.Clientes, seg => new { empresa = seg.Empresa, cliente = seg.Número, contacto = seg.Contacto }, cli => new { empresa = cli.Empresa, cliente = cli.Nº_Cliente, contacto = cli.Contacto }, (seg, cli) => new { cli.CodPostal })
                    .Select(c => c.CodPostal);
                IQueryable<CodigoPostalSeguimientoLookup> listaCodigosPostales = db.CodigosPostales
                    .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && (p.Vendedor == vendedor || listaPorGrupo.Contains(p.Número))
                    && !listaVisitados.Contains(p.Número))
                    .Select(p => new CodigoPostalSeguimientoLookup 
                {
                    CodigoPostal = p.Número.Trim(), 
                    Poblacion = p.Descripción != null ? p.Descripción.Trim() : string.Empty
                });                                
                
                return await listaCodigosPostales.ToListAsync();
            }            
        }

        public async Task<ICollection<ClienteSeguimientoLookup>> ClientesSinVisitar(string vendedor, string codigoPostal, DateTime fechaDesde, DateTime fechaHasta)
        {
            using (NVEntities db = new NVEntities())
            {
                var listaPorGrupo = db.VendedoresClientesGruposProductos
                    .Where(g => g.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && g.Vendedor == vendedor)
                    .Join(db.Clientes, vend => new { empresa = vend.Empresa, cliente = vend.Cliente, contacto = vend.Contacto }, cli => new { empresa = cli.Empresa, cliente = cli.Nº_Cliente, contacto = cli.Contacto }, (vend, cli) => new { empresa = vend.Empresa, cliente = vend.Cliente, contacto = vend.Contacto, codigoPostal = cli.CodPostal, estado = cli.Estado })
                    .Where(c => c.codigoPostal == codigoPostal && c.estado >= Constantes.Clientes.Estados.VISITA_PRESENCIAL && c.estado != Constantes.Clientes.Estados.COMISIONA_SIN_VISITA)
                    .Select(c => new { c.empresa, c.cliente, c.contacto });
                var listaVisitados = db.SeguimientosClientes
                    .Where(s => s.Vendedor == vendedor && s.Tipo == Constantes.SeguimientosCliente.Tipos.TIPO_VISITA_PRESENCIAL && s.Fecha >= fechaDesde && s.Fecha <= fechaHasta)
                    .Join(db.Clientes, seg => new { empresa = seg.Empresa, cliente = seg.Número, contacto = seg.Contacto }, cli => new { empresa = cli.Empresa, cliente = cli.Nº_Cliente, contacto = cli.Contacto }, (seg, cli) => new { empresa = cli.Empresa, cliente = cli.Nº_Cliente, contacto = cli.Contacto, codigoPostal = cli.CodPostal })
                    .Where(c => c.codigoPostal == codigoPostal)
                    .Select(c => new { c.empresa, c.cliente, c.contacto });
                var listaClientes = db.Clientes
                    .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.CodPostal == codigoPostal 
                    && p.Estado >= Constantes.Clientes.Estados.VISITA_PRESENCIAL && p.Estado != Constantes.Clientes.Estados.COMISIONA_SIN_VISITA
                    && (p.Vendedor == vendedor || listaPorGrupo.Contains(new { empresa = p.Empresa, cliente = p.Nº_Cliente, contacto = p.Contacto }))
                    && !listaVisitados.Contains(new { empresa = p.Empresa, cliente = p.Nº_Cliente, contacto = p.Contacto }))
                    .Select(p => new ClienteSeguimientoLookup
                    {
                        Empresa = p.Empresa.Trim(),
                        Cliente = p.Nº_Cliente.Trim(),
                        Contacto = p.Contacto.Trim(),
                        Nombre = p.Nombre != null ? p.Nombre.Trim() : string.Empty,
                        Direccion = p.Dirección != null ? p.Dirección.Trim() : string.Empty,
                        Estado = (short)p.Estado
                    });
                    
                var resultadoFinal = await listaClientes.ToListAsync();

                foreach (var cliente in resultadoFinal)
                {
                    var ultimoSeguimiento = await db.SeguimientosClientes.OrderByDescending(s => s.Fecha_Modificación).FirstOrDefaultAsync(s => s.Empresa == cliente.Empresa && s.Número == cliente.Cliente && s.Contacto == cliente.Contacto &&
                        s.Tipo == Constantes.SeguimientosCliente.Tipos.TIPO_VISITA_PRESENCIAL && s.Vendedor == vendedor);
                    var fecha = ultimoSeguimiento?.Fecha_Modificación;
                    cliente.FechaUltimaVisita = fecha == null ? DateTime.MinValue : (DateTime)fecha;
                    Cliente clienteCompleto = await db.Clientes.Include(c => c.CondPagoClientes).Where(c => c.Empresa == cliente.Empresa && c.Nº_Cliente == cliente.Cliente && c.Contacto == cliente.Contacto).SingleAsync();
                    var ayer = DateTime.Today.AddDays(-1);
                    if (clienteCompleto.CondPagoClientes.Where(c => c.PlazosPago == Constantes.PlazosPago.CONTADO_RIGUROSO).Any())
                    {
                        cliente.NivelRiesgoPagos = Constantes.NivelRiesgoPagos.CONTADO_RIGUROSO;
                    } 
                    else if (db.ExtractosCliente.Where(e => e.Número == cliente.Cliente && e.Contacto == cliente.Contacto && e.ImportePdte > 0 && e.TipoApunte == Constantes.TiposExtractoCliente.IMPAGADO).Any())
                    {
                        cliente.NivelRiesgoPagos = Constantes.NivelRiesgoPagos.TIENE_IMPAGADOS_PENDIENTES;
                    }
                    else if (db.ExtractosCliente.Where(e => e.Número == cliente.Cliente && e.Contacto == cliente.Contacto && e.ImportePdte > 0 && e.FechaVto < ayer).Any())
                    {
                        cliente.NivelRiesgoPagos = Constantes.NivelRiesgoPagos.TIENE_DEUDA_VENCIDA;
                    }
                    else if (db.ExtractosCliente.Where(e => e.Número == cliente.Cliente && e.Contacto == cliente.Contacto && e.ImportePdte > 0).Any())
                    {
                        cliente.NivelRiesgoPagos = Constantes.NivelRiesgoPagos.TIENE_DEUDA_NO_VENCIDA;
                    }
                    else
                    {
                        cliente.NivelRiesgoPagos = Constantes.NivelRiesgoPagos.NO_TIENE_DEUDA;
                    }
                }

                return resultadoFinal.OrderBy(r => r.FechaUltimaVisita).ToList();
                
            }
        }
    }
}