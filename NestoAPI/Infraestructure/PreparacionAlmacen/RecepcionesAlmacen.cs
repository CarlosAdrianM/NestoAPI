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
        /// <summary>
        /// Null de verdad. En un ensayo, la estrategia hace lo mismo pero en una transacción que se deshace SIEMPRE, apunta
        /// aquí las filas que toca antes y después y no avisa a nadie.
        /// </summary>
        public RegistroEnsayoRecepcion Ensayo { get; set; }
    }

    /// <summary>
    /// El ensayo de una recepción (como el de las salidas, NestoAPI#556): las filas que toca, leídas DENTRO de la transacción
    /// que se va a deshacer, antes de escribir nada y al acabar. Si falla a medias, se queda con las de antes.
    /// </summary>
    public class RegistroEnsayoRecepcion
    {
        private Func<Task<List<FilaEnsayoDTO>>> foto;

        public List<FilaEnsayoDTO> Antes { get; private set; }
        public List<FilaEnsayoDTO> Despues { get; private set; }

        /// <param name="foto">Cómo leer las filas implicadas (las claves se fijan antes de tocar nada).</param>
        public async Task Empezar(Func<Task<List<FilaEnsayoDTO>>> foto)
        {
            this.foto = foto;
            Antes = foto == null ? null : await foto().ConfigureAwait(false);
        }

        public async Task Acabar()
        {
            if (foto != null)
            {
                Despues = await foto().ConfigureAwait(false);
            }
        }
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
        /// <param name="ensayo">Solo Admin o Dirección: lo mismo, pero se deshace siempre y devuelve las filas antes y después.</param>
        Task<ResultadoTerminarRecepcionDTO> Terminar(string tipo, string empresa, string almacen, string documento,
            TerminarRecepcionDTO terminar, IPrincipal usuario, bool ensayo = false);
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
                Cuadra = CasadorEscaneos.EstaCompleto(diferencias),
                Recuperadas = TextosRecuperadas(esperado, diferencias)
            };
        }

        public async Task<ResultadoTerminarRecepcionDTO> Terminar(string tipo, string empresa, string almacen, string documento,
            TerminarRecepcionDTO terminar, IPrincipal usuario, bool ensayo = false)
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
            if (ensayo && !ServicioSalidas.PuedeEnsayar(usuario))
            {
                throw new UnauthorizedAccessException(ServicioSalidas.MENSAJE_ENSAYO_SIN_PERMISO);
            }
            // Quien ensaya (Admin o Dirección) no tiene por qué poder terminar de verdad ese tipo: como en las salidas
            if (!ensayo && !origen.PuedeTerminar(usuario, empresa?.Trim(), almacen?.Trim()))
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

            var solicitud = new SolicitudTerminarRecepcion
            {
                Empresa = Limpio(empresa),
                Almacen = Limpio(almacen)?.ToUpperInvariant(),
                Documento = Limpio(documento),
                IdRecepcion = terminar.IdRecepcion,
                Lecturas = lecturas,
                Usuario = nombre.Trim(),
                Principal = usuario,
                Dispositivo = terminar.Dispositivo
            };
            return ensayo
                ? await Ensayar(origen, solicitud).ConfigureAwait(false)
                : await origen.Terminar(solicitud).ConfigureAwait(false);
        }

        /// <summary>
        /// El ensayo: EXACTAMENTE lo mismo que terminar de verdad (C#, triggers, procedimientos y restricciones), en una
        /// transacción que la estrategia deshace siempre, con las filas implicadas antes y después. Si algo falla, no lanza:
        /// devuelve el error real para verlo.
        /// </summary>
        private static async Task<ResultadoTerminarRecepcionDTO> Ensayar(IOrigenRecepcion origen, SolicitudTerminarRecepcion solicitud)
        {
            var registro = new RegistroEnsayoRecepcion();
            solicitud.Ensayo = registro;
            try
            {
                ResultadoTerminarRecepcionDTO resultado = await origen.Terminar(solicitud).ConfigureAwait(false);
                resultado.Ensayo = true;
                resultado.FilasAntes = registro.Antes;
                resultado.FilasDespues = registro.Despues;
                resultado.Avisos = resultado.Avisos ?? new List<string>();
                resultado.Avisos.Insert(0, "ENSAYO: no se ha guardado nada ni se ha avisado a nadie.");
                return resultado;
            }
            catch (Exception ex)
            {
                string error = ServicioSalidas.MensajeCompleto(ex);
                return new ResultadoTerminarRecepcionDTO
                {
                    Tipo = origen.Tipo,
                    Documento = solicitud.Documento,
                    Ensayo = true,
                    FilasAntes = registro.Antes,
                    ErrorEnsayo = error,
                    Avisos = new List<string> { "ENSAYO: ha fallado y no se ha guardado nada. " + error }
                };
            }
        }

        /// <summary>
        /// Lo que sobra de un producto que se dio por no servido hace poco: entra con ese pedido, la línea más antigua
        /// primero (como lo reparte después PlanificadorRecepcionCompra).
        /// </summary>
        internal static List<string> TextosRecuperadas(RecepcionDTO esperado, IEnumerable<DiferenciaPreparacionDTO> diferencias)
        {
            var textos = new List<string>();
            foreach (DiferenciaPreparacionDTO diferencia in diferencias.Where(d => d.Diferencia > 0))
            {
                LineaRecepcionDTO linea = esperado.Lineas.FirstOrDefault(l =>
                    string.Equals(l.Producto?.Trim(), diferencia.Producto, StringComparison.OrdinalIgnoreCase));
                int sobra = diferencia.Diferencia;
                foreach (RecuperableRecepcionDTO recuperable in linea?.Recuperables ?? new List<RecuperableRecepcionDTO>())
                {
                    if (sobra == 0)
                    {
                        break;
                    }
                    int entra = Math.Min(sobra, recuperable.Cantidad);
                    sobra -= entra;
                    textos.Add(new LineaRecuperada
                    {
                        Pedido = recuperable.Pedido,
                        Producto = diferencia.Producto,
                        Cantidad = entra,
                        FechaNoServido = recuperable.FechaNoServido
                    }.Texto);
                }
            }
            return textos;
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
