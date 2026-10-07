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
        /// <summary>Las filas a mitad de camino, cuando la estrategia ajusta algo antes de contabilizar (REPO con diferencias).</summary>
        public List<FilaEnsayoDTO> TrasAjustar { get; private set; }

        /// <param name="foto">Cómo leer las filas implicadas (las claves se fijan antes de tocar nada).</param>
        public async Task Empezar(Func<Task<List<FilaEnsayoDTO>>> foto)
        {
            this.foto = foto;
            Antes = foto == null ? null : await foto().ConfigureAwait(false);
        }

        public async Task FotografiarTrasAjustar()
        {
            if (foto != null)
            {
                TrasAjustar = await foto().ConfigureAwait(false);
            }
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
    /// NestoAPI#600: lo que dice al mozo un tipo de recepción, para que las apps (Ariadna, Nesto) no miren el código del
    /// tipo. Cada estrategia devuelve el suyo (una clase que hereda de esta y cambia lo que haga falta); lo que no cambia
    /// se queda con el texto genérico de aquí, así que un tipo nuevo sale completo sin tocar nada más. El núcleo
    /// (<see cref="ServicioRecepciones"/>) lo copia a los DTO. El título de cada recepción depende de sus datos (el nombre
    /// del proveedor, el almacén de origen): lo pone la estrategia al leerla; si no lo pone, el núcleo usa «TIPO documento».
    /// </summary>
    public class TextosTipoRecepcion
    {
        /// <summary>Los textos genéricos (los de un tipo que no dice nada propio).</summary>
        public static readonly TextosTipoRecepcion Genericos = new TextosTipoRecepcion();

        /// <summary>La línea de debajo del título en la lista: «2 líneas · 3 ud. · 06/10».</summary>
        public virtual string Detalle(RecepcionPendienteDTO pendiente)
        {
            return Juntar(Contar(pendiente.Lineas, "línea", "líneas"), $"{pendiente.Unidades} ud.", Fecha(pendiente.Fecha));
        }

        /// <summary>El encabezado de la confirmación antes de terminar.</summary>
        public virtual string TituloConfirmacion(RecepcionDTO recepcion) => "¿Terminar la recepción con esto?";

        /// <summary>Confirmación: lo leído coincide con lo esperado (sin faltas, sobras ni productos que no tocan).</summary>
        public virtual string AvisoCoincide => "Lo leído coincide con lo esperado.";
        /// <summary>Confirmación: lo leído no coincide (hay faltas, sobras o productos que no tocan).</summary>
        public virtual string AvisoNoCoincide => "Lo leído no coincide con lo esperado.";
        /// <summary>Confirmación, además, si falta algo. Null: nada que añadir.</summary>
        public virtual string AvisoConFaltas => null;
        /// <summary>Confirmación, además, si sobra algo (lo que no es recuperado). Null: nada que añadir.</summary>
        public virtual string AvisoConSobras => null;
        /// <summary>Confirmación, además, si algo de lo que sobra es lo dado por no servido hace poco. Null: nada que añadir.</summary>
        public virtual string AvisoConRecuperadas => null;
        /// <summary>Confirmación, además, si se ha leído algo que no se esperaba. Null: nada que añadir.</summary>
        public virtual string AvisoConAjenos => null;

        /// <summary>Al terminar: qué va a aparecer en Ubicar. Null si el tipo no dice nada.</summary>
        public virtual string AvisoUbicar(ResultadoTerminarRecepcionDTO resultado) => null;

        internal static string Contar(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

        internal static string Fecha(DateTime? fecha) => fecha?.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);

        internal static string Juntar(params string[] partes) => string.Join(" · ", partes.Where(p => !string.IsNullOrWhiteSpace(p)));
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
        /// <summary>NestoAPI#600: lo que dice este tipo al mozo (lo copia el núcleo a los DTO). Null = los genéricos.</summary>
        TextosTipoRecepcion Textos { get; }
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
        private readonly Productos.IRepositorioCodigosBarras codigos;

        /// <param name="codigos">NestoAPI#605: para buscar también por los códigos alternativos. Sin él, solo número y principal.</param>
        public ServicioRecepciones(IEnumerable<IOrigenRecepcion> origenes, Productos.IRepositorioCodigosBarras codigos = null)
        {
            this.origenes = (origenes ?? Enumerable.Empty<IOrigenRecepcion>())
                .ToDictionary(o => o.Tipo.Trim().ToUpperInvariant(), o => o);
            this.codigos = codigos;
        }

        public async Task<List<RecepcionPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            var pendientes = new List<RecepcionPendienteDTO>();
            foreach (IOrigenRecepcion origen in origenes.Values)
            {
                pendientes.AddRange(Completar(origen, await origen.LeerPendientes(empresa, almacen).ConfigureAwait(false)));
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
            // NestoAPI#605: un código alternativo se busca por el número de los productos que lo tienen (cada tipo busca por número)
            var buscados = new List<string> { limpio };
            if (codigos != null)
            {
                buscados.AddRange((await codigos.ProductosConCodigo(empresa, limpio).ConfigureAwait(false) ?? new List<Productos.ProductoPorCodigoBarrasDTO>())
                    .Select(p => p.Producto?.Trim())
                    .Where(p => !string.IsNullOrEmpty(p)));
            }
            var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IOrigenRecepcion origen in origenes.Values)
            {
                foreach (string buscado in buscados.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (RecepcionPendienteDTO pendiente in Completar(origen, await origen.BuscarPorCodigo(empresa, almacen, buscado).ConfigureAwait(false)))
                    {
                        if (vistas.Add($"{pendiente.Tipo}|{pendiente.Documento}"))
                        {
                            encontradas.Add(pendiente);
                        }
                    }
                }
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
                Completar(origen, recepcion);
            }
            return recepcion;
        }

        /// <summary>
        /// NestoAPI#600: el título y el detalle de cada recepción de la lista, ya resueltos. Lo que la estrategia no ha
        /// puesto, de sus <see cref="IOrigenRecepcion.Textos"/> (o los genéricos).
        /// </summary>
        private static List<RecepcionPendienteDTO> Completar(IOrigenRecepcion origen, List<RecepcionPendienteDTO> pendientes)
        {
            TextosTipoRecepcion textos = TextosDe(origen);
            foreach (RecepcionPendienteDTO pendiente in pendientes ?? new List<RecepcionPendienteDTO>())
            {
                pendiente.Tipo = string.IsNullOrWhiteSpace(pendiente.Tipo) ? origen.Tipo : pendiente.Tipo;
                pendiente.Titulo = Limpio(pendiente.Titulo) is string titulo && titulo.Length > 0 ? titulo : $"{pendiente.Tipo} {pendiente.Documento}";
                pendiente.Detalle = Limpio(pendiente.Detalle) is string detalle && detalle.Length > 0
                    ? detalle
                    : Texto(textos, t => t.Detalle(pendiente));
            }
            return pendientes ?? new List<RecepcionPendienteDTO>();
        }

        /// <summary>NestoAPI#600: los textos del documento abierto (título y confirmación por caso), de su tipo.</summary>
        private static void Completar(IOrigenRecepcion origen, RecepcionDTO recepcion)
        {
            TextosTipoRecepcion textos = TextosDe(origen);
            recepcion.Titulo = Limpio(recepcion.Titulo) is string titulo && titulo.Length > 0 ? titulo : $"{recepcion.Tipo} {recepcion.Documento}";
            recepcion.TituloConfirmacion = Texto(textos, t => t.TituloConfirmacion(recepcion));
            recepcion.AvisoCoincide = Texto(textos, t => t.AvisoCoincide);
            recepcion.AvisoNoCoincide = Texto(textos, t => t.AvisoNoCoincide);
            recepcion.AvisoConFaltas = Texto(textos, t => t.AvisoConFaltas);
            recepcion.AvisoConSobras = Texto(textos, t => t.AvisoConSobras);
            recepcion.AvisoConRecuperadas = Texto(textos, t => t.AvisoConRecuperadas);
            recepcion.AvisoConAjenos = Texto(textos, t => t.AvisoConAjenos);
        }

        /// <summary>
        /// NestoAPI#600: lo que se le enseña al mozo al terminar, entero: qué ha entrado, lo que no, las diferencias, los
        /// avisos y qué va a aparecer en Ubicar (de su tipo). La app lo enseña tal cual.
        /// </summary>
        private static ResultadoTerminarRecepcionDTO Completar(IOrigenRecepcion origen, ResultadoTerminarRecepcionDTO resultado)
        {
            if (resultado == null)
            {
                return null;
            }
            resultado.Tipo = string.IsNullOrWhiteSpace(resultado.Tipo) ? origen.Tipo : resultado.Tipo;
            if (!resultado.YaEstabaTerminada && resultado.ErrorEnsayo == null && string.IsNullOrWhiteSpace(resultado.AvisoUbicar))
            {
                resultado.AvisoUbicar = Texto(TextosDe(origen), t => t.AvisoUbicar(resultado));
            }
            resultado.Mensaje = MensajeTerminada(resultado);
            return resultado;
        }

        internal static string MensajeTerminada(ResultadoTerminarRecepcionDTO resultado)
        {
            if (resultado.YaEstabaTerminada)
            {
                return "Esta recepción ya estaba terminada: no se ha repetido nada.";
            }
            List<string> avisos = resultado.Avisos ?? new List<string>();
            if (resultado.ErrorEnsayo != null)
            {
                return string.Join(Environment.NewLine, avisos);
            }
            List<DocumentoRecepcionDTO> documentos = resultado.Documentos ?? new List<DocumentoRecepcionDTO>();
            var lineas = new List<string>
            {
                documentos.Count == 0
                    ? "Recepción terminada."
                    : "Recibido. " + string.Join(", ", documentos.Select(d => $"albarán {d.Albaran} (pedido {d.Pedido})")) + "."
            };
            List<DiferenciaPreparacionDTO> noEsperados = resultado.NoEsperados ?? new List<DiferenciaPreparacionDTO>();
            if (noEsperados.Count > 0)
            {
                lineas.Add("No han entrado (no se esperaban): " + string.Join(", ", noEsperados.Select(n => $"{n.Producto} ({n.Leido})")) + ".");
            }
            List<DiferenciaPreparacionDTO> diferencias = resultado.Diferencias ?? new List<DiferenciaPreparacionDTO>();
            if (diferencias.Count > 0)
            {
                lineas.Add("No coincide con lo enviado:");
                lineas.AddRange(diferencias.Select(d => d.Ajeno
                    ? $"  {d.Producto}: no venía, leído {d.Leido}"
                    : $"  {d.Producto}: enviado {d.Esperado}, leído {d.Leido} ({(d.Leido > d.Esperado ? "sobra" : "falta")} {Math.Abs(d.Leido - d.Esperado)})"));
            }
            lineas.AddRange((resultado.Recuperadas ?? new List<LineaRecuperadaDTO>())
                .Select(r => r.Texto ?? $"{r.Cantidad} ud. de {r.Producto} eran del pedido {r.Pedido}: entran con ese pedido."));
            lineas.AddRange(avisos);
            if (!string.IsNullOrWhiteSpace(resultado.AvisoUbicar))
            {
                lineas.Add(resultado.AvisoUbicar);
            }
            return string.Join(Environment.NewLine, lineas);
        }

        private static TextosTipoRecepcion TextosDe(IOrigenRecepcion origen) => origen.Textos ?? TextosTipoRecepcion.Genericos;

        /// <summary>El texto del tipo; si no dice nada (null o en blanco), el genérico (que también puede ser null).</summary>
        private static string Texto(TextosTipoRecepcion textos, Func<TextosTipoRecepcion, string> leer)
        {
            string propio = Limpio(leer(textos));
            return string.IsNullOrEmpty(propio) ? Limpio(leer(TextosTipoRecepcion.Genericos)) is string generico && generico.Length > 0 ? generico : null : propio;
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
            return Completar(origen, ensayo
                ? await Ensayar(origen, solicitud).ConfigureAwait(false)
                : await origen.Terminar(solicitud).ConfigureAwait(false));
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
                resultado.FilasTrasAjustar = registro.TrasAjustar;
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
                    FilasTrasAjustar = registro.TrasAjustar,
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
