using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.PlanesVentajas;

namespace NestoAPI.Infraestructure.PlanesVentajas
{
    public class PlanesVentajasService : IPlanesVentajasService
    {
        // Coincide con la constante ESTADO_PLAN_CANCELADO del cliente VB.
        private const int ESTADO_PLAN_CANCELADO = 6;
        private const string EMPRESA_FILTRO_CLIENTES = "1";

        private readonly NVEntities db;

        public PlanesVentajasService()
        {
            db = new NVEntities();
        }

        internal PlanesVentajasService(NVEntities db)
        {
            this.db = db;
        }

        public async Task<List<EstadoPlanVentajasDTO>> ListarEstadosAsync()
        {
            return await db.EstadosPlanesVentajas
                .OrderBy(e => e.Numero)
                .Select(e => new EstadoPlanVentajasDTO
                {
                    Numero = e.Numero,
                    Descripcion = e.Descripcion
                })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<EmpresaResumenDTO>> ListarEmpresasAsync()
        {
            return await db.Empresas
                .OrderBy(e => e.Número)
                .Select(e => new EmpresaResumenDTO
                {
                    Numero = e.Número,
                    Nombre = e.Nombre
                })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<PlanVentajasDTO>> ListarPlanesAsync(string vendedor, string filtroCliente, bool incluirCancelados)
        {
            IQueryable<PlanVentajas> query = db.PlanesVentajas;

            if (!incluirCancelados)
            {
                query = query.Where(p => p.Estado != ESTADO_PLAN_CANCELADO);
            }

            bool hayFiltroCliente = !string.IsNullOrWhiteSpace(filtroCliente);
            bool hayVendedor = !string.IsNullOrWhiteSpace(vendedor);

            if (hayFiltroCliente)
            {
                query = query.Where(p => db.PlanesVentajasClientes
                    .Any(pvc => pvc.NumeroContrato == p.Numero
                                && db.Clientes.Any(c =>
                                    c.Empresa == EMPRESA_FILTRO_CLIENTES
                                    && c.Estado >= 0
                                    && c.Nº_Cliente == pvc.Cliente
                                    && c.Nº_Cliente == filtroCliente)));
            }
            else if (hayVendedor)
            {
                query = query.Where(p => db.PlanesVentajasClientes
                    .Any(pvc => pvc.NumeroContrato == p.Numero
                                && db.Clientes.Any(c =>
                                    c.Empresa == EMPRESA_FILTRO_CLIENTES
                                    && c.Estado >= 0
                                    && c.Nº_Cliente == pvc.Cliente
                                    && c.Vendedor.Trim() == vendedor.Trim())));
            }

            // NestoAPI#219: NO ordenar en la consulta SQL. Con los joins de navegación (Empresa1,
            // EstadosPlanVentaja) y las subconsultas Any() del filtro, EF6 añade columnas de clave al
            // ORDER BY y duplica 'Número' (clave de PlanVentajas y columna de Empresa), lo que SQL Server
            // rechaza ("La columna 'Número' se ha especificado varias veces"). Ordenamos en memoria tras
            // materializar (el conjunto de planes es pequeño).
            var lista = await query
                .Select(p => new PlanVentajasDTO
                {
                    Numero = p.Numero,
                    Empresa = p.Empresa,
                    EmpresaNombre = p.Empresa1.Nombre,
                    FechaInicio = p.FechaInicio,
                    FechaFin = p.FechaFin,
                    Importe = p.Importe,
                    Familia = p.Familia,
                    Estado = p.Estado,
                    EstadoDescripcion = p.EstadosPlanVentaja.Descripcion,
                    Comentarios = p.Comentarios,
                    Clientes = p.PlanVentajasClientes.Select(pvc => pvc.Cliente).ToList()
                })
                .ToListAsync()
                .ConfigureAwait(false);

            return lista.OrderBy(d => d.FechaFin).ToList();
        }

        public async Task<PlanVentajasDTO> ObtenerPlanAsync(int numero)
        {
            return await db.PlanesVentajas
                .Where(p => p.Numero == numero)
                .Select(p => new PlanVentajasDTO
                {
                    Numero = p.Numero,
                    Empresa = p.Empresa,
                    EmpresaNombre = p.Empresa1.Nombre,
                    FechaInicio = p.FechaInicio,
                    FechaFin = p.FechaFin,
                    Importe = p.Importe,
                    Familia = p.Familia,
                    Estado = p.Estado,
                    EstadoDescripcion = p.EstadosPlanVentaja.Descripcion,
                    Comentarios = p.Comentarios,
                    Clientes = p.PlanVentajasClientes.Select(pvc => pvc.Cliente).ToList()
                })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<ClientePlanVentajasDTO>> ObtenerClientesAsync(int numero, string empresa)
        {
            string empresaFiltro = string.IsNullOrWhiteSpace(empresa) ? EMPRESA_FILTRO_CLIENTES : empresa;

            return await (from pvc in db.PlanesVentajasClientes
                          join c in db.Clientes on pvc.Cliente equals c.Nº_Cliente
                          where pvc.NumeroContrato == numero
                                && c.Empresa == empresaFiltro
                          select new ClientePlanVentajasDTO
                          {
                              Empresa = c.Empresa,
                              NumeroCliente = c.Nº_Cliente,
                              Contacto = c.Contacto,
                              Nombre = c.Nombre,
                              Direccion = c.Dirección,
                              CodPostal = c.CodPostal,
                              Poblacion = c.Población,
                              Vendedor = c.Vendedor
                          })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<LineaVentaPlanDTO>> ObtenerLineasVentaAsync(int numero, string empresa)
        {
            string empresaFiltro = string.IsNullOrWhiteSpace(empresa) ? EMPRESA_FILTRO_CLIENTES : empresa;

            PlanVentajas plan = await db.PlanesVentajas
                .FirstOrDefaultAsync(p => p.Numero == numero)
                .ConfigureAwait(false);
            if (plan == null)
            {
                return new List<LineaVentaPlanDTO>();
            }

            DateTime fechaInicio = plan.FechaInicio;
            DateTime fechaFin = plan.FechaFin;
            string familia = plan.Familia;

            return await (from l in db.LinPedidoVtas
                          join pvc in db.PlanesVentajasClientes on l.Nº_Cliente equals pvc.Cliente
                          join c in db.Clientes on pvc.Cliente equals c.Nº_Cliente
                          where pvc.NumeroContrato == numero
                                && c.Empresa == empresaFiltro
                                && l.Contacto == c.Contacto
                                && l.Familia == familia
                                && l.Fecha_Factura >= fechaInicio
                                && l.Fecha_Factura <= fechaFin
                          orderby l.Fecha_Factura descending
                          select new LineaVentaPlanDTO
                          {
                              NumeroPedido = l.Número,
                              Producto = l.Producto,
                              Texto = l.Texto,
                              Cantidad = l.Cantidad,
                              BaseImponible = l.Base_Imponible,
                              FechaFactura = l.Fecha_Factura
                          })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<PlanVentajasDTO> CrearPlanAsync(PlanVentajasDTO plan, string usuario)
        {
            DateTime ahora = DateTime.Now;
            var entidad = new PlanVentajas
            {
                Empresa = plan.Empresa,
                FechaInicio = plan.FechaInicio,
                FechaFin = plan.FechaFin,
                Importe = plan.Importe,
                Familia = plan.Familia,
                Estado = plan.Estado,
                Comentarios = plan.Comentarios,
                Usuario = usuario,
                FechaModificacion = ahora
            };
            db.PlanesVentajas.Add(entidad);
            await db.SaveChangesAsync().ConfigureAwait(false);

            if (plan.Clientes != null)
            {
                foreach (string cliente in plan.Clientes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
                {
                    db.PlanesVentajasClientes.Add(new PlanVentajasCliente
                    {
                        NumeroContrato = entidad.Numero,
                        Cliente = cliente,
                        Usuario = usuario,
                        FechaModificacion = ahora
                    });
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            return await ObtenerPlanAsync(entidad.Numero).ConfigureAwait(false);
        }

        public async Task<PlanVentajasDTO> ActualizarPlanAsync(int numero, PlanVentajasDTO plan, string usuario)
        {
            PlanVentajas entidad = await db.PlanesVentajas
                .Include(p => p.PlanVentajasClientes)
                .FirstOrDefaultAsync(p => p.Numero == numero)
                .ConfigureAwait(false);
            if (entidad == null)
            {
                return null;
            }

            DateTime ahora = DateTime.Now;
            entidad.Empresa = plan.Empresa;
            entidad.FechaInicio = plan.FechaInicio;
            entidad.FechaFin = plan.FechaFin;
            entidad.Importe = plan.Importe;
            entidad.Familia = plan.Familia;
            entidad.Estado = plan.Estado;
            entidad.Comentarios = plan.Comentarios;
            entidad.Usuario = usuario;
            entidad.FechaModificacion = ahora;

            var clientesNuevos = (plan.Clientes ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct()
                .ToList();
            var clientesActuales = entidad.PlanVentajasClientes.ToList();

            foreach (var pvc in clientesActuales.Where(a => !clientesNuevos.Contains((a.Cliente ?? string.Empty).Trim())))
            {
                db.PlanesVentajasClientes.Remove(pvc);
            }

            var actualesSet = new HashSet<string>(clientesActuales.Select(a => (a.Cliente ?? string.Empty).Trim()));
            foreach (var cliente in clientesNuevos.Where(c => !actualesSet.Contains(c)))
            {
                db.PlanesVentajasClientes.Add(new PlanVentajasCliente
                {
                    NumeroContrato = numero,
                    Cliente = cliente,
                    Usuario = usuario,
                    FechaModificacion = ahora
                });
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            return await ObtenerPlanAsync(numero).ConfigureAwait(false);
        }
    }
}
