using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Lo que necesita una estrategia para terminar una recepción, ya validado por el núcleo.</summary>
    public class SolicitudTerminarRecepcion
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public string Documento { get; set; }
        public Guid IdRecepcion { get; set; }
        /// <summary>Unidades contadas por producto (en mayúsculas y sin espacios), todas mayores que cero.</summary>
        public Dictionary<string, int> Lecturas { get; set; }
        /// <summary>El usuario del token, tal como se graba. Nunca vacío.</summary>
        public string Usuario { get; set; }
        public IPrincipal Principal { get; set; }
        public string Dispositivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#559/#553: un TIPO de cosa que se recibe en el almacén (pedidos de compra de un proveedor,
    /// reposiciones entre almacenes…). El núcleo (<see cref="ServicioRecepciones"/>) es el mismo para todos:
    /// cada tipo dice qué se espera, quién puede terminar la recepción y qué significa terminarla.
    /// </summary>
    public interface IOrigenRecepcion
    {
        /// <summary>COMP, REPO… (4 letras, como TipoOrigen de los escaneos).</summary>
        string Tipo { get; }
        /// <summary>False mientras un tipo solo se pueda leer y comparar (su cierre sigue en otro sitio).</summary>
        bool SeTerminaDesdeAqui { get; }
        /// <summary>Quién puede terminar este tipo de recepción en ese almacén (lo decide cada tipo).</summary>
        bool PuedeTerminar(IPrincipal usuario, string empresa, string almacen);
        /// <summary>Lo pendiente de este tipo que contiene ese producto (número o código de barras).</summary>
        Task<List<RecepcionPendienteDTO>> BuscarPorCodigo(string empresa, string almacen, string codigo);
        Task<List<RecepcionPendienteDTO>> LeerPendientes(string empresa, string almacen);
        /// <summary>Null si no hay nada pendiente de recibir con ese documento.</summary>
        Task<RecepcionDTO> LeerEsperado(string empresa, string almacen, string documento);
        Task<ResultadoTerminarRecepcionDTO> Terminar(SolicitudTerminarRecepcion solicitud);
    }

    public interface IServicioRecepciones
    {
        Task<List<RecepcionPendienteDTO>> LeerPendientes(string empresa, string almacen);
        /// <summary>Lo pendiente que contiene un producto leído: para empezar a recibir sin elegir de la lista.</summary>
        Task<List<RecepcionPendienteDTO>> Buscar(string empresa, string almacen, string codigo);
        Task<RecepcionDTO> LeerEsperado(string tipo, string empresa, string almacen, string documento, IPrincipal usuario);
        Task<ResultadoCasarRecepcionDTO> Casar(string tipo, string empresa, string almacen, string documento, IEnumerable<LecturaRecepcionDTO> lecturas);
        Task<ResultadoTerminarRecepcionDTO> Terminar(string tipo, string empresa, string almacen, string documento,
            TerminarRecepcionDTO terminar, IPrincipal usuario);
    }

    /// <summary>
    /// NestoAPI#559/#553: el núcleo común de «Recibir». Valida, compara lo leído con lo esperado (igual para
    /// todos los tipos) y delega en la estrategia de cada tipo lo que es suyo: qué se espera, quién puede
    /// terminar y qué se hace al terminar. Lo usan Ariadna y Nesto.
    /// </summary>
    public class ServicioRecepciones : IServicioRecepciones
    {
        private readonly Dictionary<string, IOrigenRecepcion> origenes;

        public ServicioRecepciones(IEnumerable<IOrigenRecepcion> origenes)
        {
            this.origenes = (origenes ?? Enumerable.Empty<IOrigenRecepcion>())
                .ToDictionary(o => o.Tipo.Trim().ToUpperInvariant(), o => o);
        }

        public async Task<List<RecepcionPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            var pendientes = new List<RecepcionPendienteDTO>();
            foreach (IOrigenRecepcion origen in origenes.Values)
            {
                pendientes.AddRange(await origen.LeerPendientes(empresa, almacen).ConfigureAwait(false) ?? new List<RecepcionPendienteDTO>());
            }
            return pendientes.OrderBy(p => p.Fecha ?? DateTime.MaxValue).ThenBy(p => p.Tipo).ThenBy(p => p.Documento).ToList();
        }

        public async Task<List<RecepcionPendienteDTO>> Buscar(string empresa, string almacen, string codigo)
        {
            string limpio = codigo?.Trim();
            var encontradas = new List<RecepcionPendienteDTO>();
            if (string.IsNullOrEmpty(limpio))
            {
                return encontradas;
            }
            foreach (IOrigenRecepcion origen in origenes.Values)
            {
                encontradas.AddRange(await origen.BuscarPorCodigo(empresa, almacen, limpio).ConfigureAwait(false) ?? new List<RecepcionPendienteDTO>());
            }
            return encontradas;
        }

        public async Task<RecepcionDTO> LeerEsperado(string tipo, string empresa, string almacen, string documento, IPrincipal usuario)
        {
            IOrigenRecepcion origen = Origen(tipo);
            RecepcionDTO recepcion = await origen.LeerEsperado(empresa, almacen, documento).ConfigureAwait(false);
            if (recepcion != null)
            {
                recepcion.Tipo = origen.Tipo;
                recepcion.SeTerminaDesdeAqui = origen.SeTerminaDesdeAqui;
                recepcion.PuedeTerminar = origen.SeTerminaDesdeAqui && origen.PuedeTerminar(usuario, empresa, almacen);
            }
            return recepcion;
        }

        public async Task<ResultadoCasarRecepcionDTO> Casar(string tipo, string empresa, string almacen, string documento,
            IEnumerable<LecturaRecepcionDTO> lecturas)
        {
            IOrigenRecepcion origen = Origen(tipo);
            RecepcionDTO esperado = await origen.LeerEsperado(empresa, almacen, documento).ConfigureAwait(false);
            if (esperado == null)
            {
                return null;
            }
            List<DiferenciaPreparacionDTO> diferencias = CasadorEscaneos.Casar(
                esperado.Lineas.Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Descripcion = l.Descripcion, Unidades = l.Cantidad }),
                (lecturas ?? Enumerable.Empty<LecturaRecepcionDTO>())
                    .Where(l => !string.IsNullOrWhiteSpace(l?.Producto))
                    .Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Unidades = l.Cantidad }));
            return new ResultadoCasarRecepcionDTO
            {
                Tipo = origen.Tipo,
                Documento = esperado.Documento ?? documento,
                Productos = diferencias,
                Cuadra = CasadorEscaneos.EstaCompleto(diferencias)
            };
        }

        public async Task<ResultadoTerminarRecepcionDTO> Terminar(string tipo, string empresa, string almacen, string documento,
            TerminarRecepcionDTO terminar, IPrincipal usuario)
        {
            IOrigenRecepcion origen = Origen(tipo);
            if (!origen.SeTerminaDesdeAqui)
            {
                throw new NestoBusinessException($"Las recepciones de tipo {origen.Tipo} todavía no se pueden terminar desde aquí: " +
                    "se pueden leer y comparar, pero se terminan como hasta ahora.");
            }
            string nombre = usuario?.Identity?.IsAuthenticated == true ? usuario.Identity.Name : null;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                // Quién recibe queda grabado: sin usuario no se inventa uno
                throw new UnauthorizedAccessException("Para terminar una recepción hay que estar identificado.");
            }
            if (!origen.PuedeTerminar(usuario, empresa?.Trim(), almacen?.Trim()))
            {
                throw new UnauthorizedAccessException($"No tienes permiso para terminar recepciones de tipo {origen.Tipo}.");
            }
            if (terminar == null || terminar.IdRecepcion == Guid.Empty)
            {
                throw new NestoBusinessException("Falta el identificador de la recepción (IdRecepcion).");
            }
            Dictionary<string, int> lecturas = (terminar.Lecturas ?? new List<LecturaRecepcionDTO>())
                .Where(l => !string.IsNullOrWhiteSpace(l?.Producto))
                .GroupBy(l => PlanificadorRecepcionCompra.Normalizar(l.Producto))
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(l => l.Cantidad) })
                .Where(l => l.Cantidad > 0)
                .ToDictionary(l => l.Producto, l => l.Cantidad);
            if (!lecturas.Any())
            {
                throw new NestoBusinessException("No se ha recibido nada: no hay ninguna cantidad mayor que cero.");
            }

            return await origen.Terminar(new SolicitudTerminarRecepcion
            {
                Empresa = Limpio(empresa),
                Almacen = Limpio(almacen)?.ToUpperInvariant(),
                Documento = Limpio(documento),
                IdRecepcion = terminar.IdRecepcion,
                Lecturas = lecturas,
                Usuario = nombre.Trim(),
                Principal = usuario,
                Dispositivo = terminar.Dispositivo
            }).ConfigureAwait(false);
        }

        private IOrigenRecepcion Origen(string tipo)
        {
            string clave = tipo?.Trim().ToUpperInvariant() ?? string.Empty;
            return origenes.TryGetValue(clave, out IOrigenRecepcion origen)
                ? origen
                : throw new NestoBusinessException($"No se conoce el tipo de recepción «{tipo}». Tipos: {string.Join(", ", origenes.Keys)}.");
        }

        private static string Limpio(string texto)
        {
            return texto?.Trim();
        }
    }
}
