using NestoAPI.Infraestructure.Clientes.Embudo;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// Carlos 25/11/26: en el PICKING (el recorrido de recogida, salidas PICK y REPO de Ariadna) leer la etiqueta de la muestra pasa
    /// a ser obligatorio cuando el almacén ya tiene sus etiquetas pegadas en la gaveta, el hueco o el cajón (NestoAPI#884). Interruptor
    /// por almacén, por DATO: ParametrosUsuario empresa 1 «(defecto)» <c>Muestras.PickingLecturaObligatoria.{ALG|REI|ALC}</c>; SOLO «1»
    /// = encendido (sin fila o cualquier otra cosa: apagado, todo como hoy). Packing, Ubicar, Recibir, Enviar reposición de una tienda,
    /// inventario y lo demás no cambian. Puras: sin base de datos.
    /// </summary>
    public static class ReglaLecturaObligatoriaMuestras
    {
        public const string PREFIJO_CLAVE = "Muestras.PickingLecturaObligatoria.";
        public const string VALOR_ENCENDIDO = "1";
        public const string VALOR_APAGADO = "0";

        /// <summary>La clave del interruptor de un almacén (ALG, REI o ALC); null si el almacén no es ninguno de los tres.</summary>
        public static string Clave(string almacen)
        {
            string normalizado = ReglasMuestras.NormalizarAlmacen(almacen);
            return normalizado == null ? null : PREFIJO_CLAVE + normalizado;
        }

        /// <summary>Encendido SOLO con «1» (con espacios alrededor, también). Sin fila, vacío, «true», «sí»…: apagado.</summary>
        public static bool Interpretar(string valor) => string.Equals(valor?.Trim(), VALOR_ENCENDIDO, StringComparison.Ordinal);

        /// <summary>Los códigos válidos de la línea (el principal, el primero; los internos del 29 incluidos; #674: solo GTIN válidos).</summary>
        public static List<string> CodigosValidos(LineaPickingAlmacenDTO linea)
            => linea == null ? new List<string>() : CompletadorCodigosBarras.Juntar(linea.CodigoBarras, linea.CodigosBarras);

        /// <summary>
        /// Con el interruptor encendido, la línea es una muestra (MMP) con ALGÚN código de barras activo y válido (el de la ficha o uno
        /// de ProductosCodigosBarras, el interno incluido): hay que leerlo. Una muestra sin ningún código sigue tecleándose como hoy.
        /// </summary>
        public static bool ExigeLeer(LineaPickingAlmacenDTO linea, bool encendido)
            => encendido && linea != null && MuestrasSinCodigo.EsMuestra(linea.SubGrupoProducto) && CodigosValidos(linea).Count > 0;

        /// <summary>
        /// Aplica la regla al recorrido ya ordenado y con sus códigos completos (<see cref="CompletadorCodigosBarras"/>). Apagado, no
        /// toca NADA (exactamente como hoy). Encendido, en cada muestra con código: <c>LecturaObligatoria</c> = true,
        /// <c>SinCodigoAdmitido</c> = false y, si la ficha no tiene código (el caso del interno, que nunca va en la ficha),
        /// <c>CodigoBarras</c> = su primer código válido y <c>SinCodigo</c> = false, como cualquier producto con código.
        /// <c>PideCantidad</c> no cambia (un bip no es una unidad). Si cambia algún principal, se recalcula <c>CodigoDuplicado</c>.
        /// </summary>
        /// <returns>Cuántas líneas exigen leer.</returns>
        public static int Aplicar(IEnumerable<LineaPickingAlmacenDTO> lineas, bool encendido)
        {
            if (!encendido || lineas == null)
            {
                return 0;
            }
            List<LineaPickingAlmacenDTO> lista = lineas.Where(l => l != null).ToList();
            int obligatorias = 0;
            bool principalCambiado = false;
            foreach (LineaPickingAlmacenDTO linea in lista)
            {
                if (!ExigeLeer(linea, true))
                {
                    continue;
                }
                List<string> codigos = CodigosValidos(linea);
                obligatorias++;
                linea.LecturaObligatoria = true;
                linea.SinCodigoAdmitido = false;
                linea.CodigosBarras = codigos;
                if (ValidadorGtin.SoloSiValido(linea.CodigoBarras) == null)
                {
                    linea.CodigoBarras = codigos[0];
                    principalCambiado = true;
                }
                linea.SinCodigo = false;
            }
            if (principalCambiado)
            {
                HashSet<string> duplicados = CasadorEscaneos.CodigosDuplicados(
                    lista.Select(l => new KeyValuePair<string, string>(l.Producto, l.CodigoBarras)));
                foreach (LineaPickingAlmacenDTO linea in lista)
                {
                    linea.CodigoDuplicado = linea.CodigoBarras != null && duplicados.Contains(linea.CodigoBarras);
                }
            }
            return obligatorias;
        }

        /// <summary>«En Algete quedan 12 muestras con stock sin ningún código de barras: …». Null si no queda ninguna o está apagado.</summary>
        public static string AvisoMuestrasSinCodigo(string almacen, bool encendido, int muestrasSinCodigo)
        {
            if (!encendido || muestrasSinCodigo <= 0)
            {
                return null;
            }
            string cuantas = muestrasSinCodigo == 1
                ? "queda 1 muestra con stock sin ningún código de barras: se seguirá tecleando"
                : $"quedan {muestrasSinCodigo} muestras con stock sin ningún código de barras: se seguirán tecleando";
            return $"En {NombreAlmacen(almacen)} {cuantas} hasta que tengan su código interno y su etiqueta en el cajón o la gaveta.";
        }

        internal static string NombreAlmacen(string almacen)
        {
            switch (ReglasMuestras.NormalizarAlmacen(almacen))
            {
                case Constantes.Almacenes.ALGETE:
                    return "Algete";
                case Constantes.Almacenes.REINA:
                    return "Reina";
                case Constantes.Almacenes.ALCOBENDAS:
                    return "Alcobendas";
                default:
                    return almacen?.Trim();
            }
        }

        /// <summary>Quién consulta y cambia el interruptor: Almacén, Dirección e Informática.</summary>
        public static bool PuedeGestionar(IPrincipal usuario)
            => usuario != null && new[] { Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA }
                .Any(g => usuario.IsInRoleSinDominio(g));

        public const string MENSAJE_SIN_PERMISO =
            "Para consultar o cambiar la lectura obligatoria de las muestras hay que ser de Almacén, Dirección o Informática.";
    }

    /// <summary>El interruptor por almacén, con caché corta. Registrado como singleton (la caché es de la instancia).</summary>
    public interface IInterruptorLecturaMuestras
    {
        /// <summary>Si el almacén exige leer la etiqueta de las muestras en el picking. Nunca lanza: si no se puede leer, apagado.</summary>
        Task<bool> Encendido(string almacen, bool sinCache = false);

        /// <summary>Escribe la fila «(defecto)» (crea o cambia), deja rastro en Modificaciones e invalida la caché.</summary>
        Task Cambiar(string almacen, bool encendido, string usuario);

        void Invalidar();
    }

    public class InterruptorLecturaMuestras : IInterruptorLecturaMuestras
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(2);

        private readonly IParametrosEmbudoDiario parametros;
        private readonly Func<DateTime> ahora;
        private readonly ConcurrentDictionary<string, Tuple<bool, DateTime>> cache = new ConcurrentDictionary<string, Tuple<bool, DateTime>>(StringComparer.OrdinalIgnoreCase);

        /// <param name="parametros">Lectura y escritura de ParametrosUsuario «(defecto)» de la empresa 1 (sin crear filas al leer). Sin él, EF.</param>
        public InterruptorLecturaMuestras(IParametrosEmbudoDiario parametros = null, Func<DateTime> ahora = null)
        {
            this.parametros = parametros ?? new ParametrosEmbudoDiarioEF();
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public Task<bool> Encendido(string almacen, bool sinCache = false)
        {
            string clave = ReglaLecturaObligatoriaMuestras.Clave(almacen);
            if (clave == null)
            {
                return Task.FromResult(false);
            }
            DateTime momento = ahora();
            if (!sinCache && cache.TryGetValue(clave, out Tuple<bool, DateTime> guardado) && guardado.Item2 > momento)
            {
                return Task.FromResult(guardado.Item1);
            }
            bool encendido;
            try
            {
                encendido = ReglaLecturaObligatoriaMuestras.Interpretar(parametros.Leer(clave));
            }
            catch (Exception ex)
            {
                // Apagado = como hoy: la muestra se sigue tecleando
                ElmahHelper.Log(new Exception($"[Lectura obligatoria de muestras] No se ha podido leer {clave}: se deja apagado. {ex.Message}", ex));
                return Task.FromResult(false);
            }
            cache[clave] = Tuple.Create(encendido, momento.Add(DURACION_CACHE));
            return Task.FromResult(encendido);
        }

        public async Task Cambiar(string almacen, bool encendido, string usuario)
        {
            string clave = ReglaLecturaObligatoriaMuestras.Clave(almacen) ?? throw new ArgumentException(ReglasMuestras.MensajeAlmacen);
            try
            {
                await parametros.Escribir(clave, encendido ? ReglaLecturaObligatoriaMuestras.VALOR_ENCENDIDO : ReglaLecturaObligatoriaMuestras.VALOR_APAGADO,
                    usuario, encendido ? "Lectura obligatoria de la etiqueta de las muestras en el picking: encendida." : "Lectura obligatoria de la etiqueta de las muestras en el picking: apagada.")
                    .ConfigureAwait(false);
            }
            finally
            {
                Invalidar();
            }
        }

        public void Invalidar() => cache.Clear();
    }

    /// <summary>GET api/Almacen/Muestras/LecturaObligatoria: una fila por almacén.</summary>
    public class EstadoLecturaObligatoriaMuestrasDTO
    {
        public string Almacen { get; set; }
        public bool Encendido { get; set; }
        /// <summary>Muestras activas (MMP, Estado ≥ 0) con stock en el almacén y algún código válido (el interno incluido): con el interruptor encendido, se leen.</summary>
        public int MuestrasConCodigo { get; set; }
        /// <summary>Las mismas sin ningún código: se siguen tecleando.</summary>
        public int MuestrasSinCodigo { get; set; }
    }

    /// <summary>PUT api/Almacen/Muestras/LecturaObligatoria.</summary>
    public class CambioLecturaObligatoriaMuestrasDTO
    {
        public string Almacen { get; set; }
        public bool Encendido { get; set; }
    }

    public class ResultadoCambioLecturaObligatoriaMuestrasDTO : EstadoLecturaObligatoriaMuestrasDTO
    {
        /// <summary>False si ya estaba así (no se escribe nada ni deja rastro).</summary>
        public bool Cambiado { get; set; }
        /// <summary>Al encender: cuántas muestras con stock siguen sin código (se seguirán tecleando). Null si ninguna.</summary>
        public string Aviso { get; set; }
        public string Mensaje { get; set; }
    }

    public interface IServicioLecturaObligatoriaMuestras
    {
        Task<List<EstadoLecturaObligatoriaMuestrasDTO>> Leer(string empresa);

        /// <exception cref="ArgumentException">El almacén no es ALG, REI ni ALC.</exception>
        Task<ResultadoCambioLecturaObligatoriaMuestrasDTO> Cambiar(string empresa, CambioLecturaObligatoriaMuestrasDTO cambio, string usuario);
    }

    public class ServicioLecturaObligatoriaMuestras : IServicioLecturaObligatoriaMuestras
    {
        private readonly IInterruptorLecturaMuestras interruptor;
        private readonly IRepositorioMuestras muestras;

        public ServicioLecturaObligatoriaMuestras(IInterruptorLecturaMuestras interruptor, IRepositorioMuestras muestras)
        {
            this.interruptor = interruptor ?? throw new ArgumentNullException(nameof(interruptor));
            this.muestras = muestras ?? throw new ArgumentNullException(nameof(muestras));
        }

        public async Task<List<EstadoLecturaObligatoriaMuestrasDTO>> Leer(string empresa)
        {
            var resultado = new List<EstadoLecturaObligatoriaMuestrasDTO>();
            foreach (string almacen in ReglasMuestras.ALMACENES)
            {
                var estado = new EstadoLecturaObligatoriaMuestrasDTO
                {
                    Almacen = almacen,
                    Encendido = await interruptor.Encendido(almacen, sinCache: true).ConfigureAwait(false)
                };
                await Contar(empresa, almacen, estado).ConfigureAwait(false);
                resultado.Add(estado);
            }
            return resultado;
        }

        public async Task<ResultadoCambioLecturaObligatoriaMuestrasDTO> Cambiar(string empresa, CambioLecturaObligatoriaMuestrasDTO cambio, string usuario)
        {
            string almacen = ReglasMuestras.NormalizarAlmacen(cambio?.Almacen) ?? throw new ArgumentException(ReglasMuestras.MensajeAlmacen);
            bool antes = await interruptor.Encendido(almacen, sinCache: true).ConfigureAwait(false);
            bool cambiado = antes != cambio.Encendido;
            if (cambiado)
            {
                await interruptor.Cambiar(almacen, cambio.Encendido, usuario).ConfigureAwait(false);
            }
            var resultado = new ResultadoCambioLecturaObligatoriaMuestrasDTO { Almacen = almacen, Encendido = cambio.Encendido, Cambiado = cambiado };
            await Contar(empresa, almacen, resultado).ConfigureAwait(false);
            resultado.Aviso = ReglaLecturaObligatoriaMuestras.AvisoMuestrasSinCodigo(almacen, cambio.Encendido, resultado.MuestrasSinCodigo);
            string nombre = ReglaLecturaObligatoriaMuestras.NombreAlmacen(almacen);
            resultado.Mensaje = !cambiado
                ? $"En {nombre} la lectura obligatoria de las muestras ya estaba {(cambio.Encendido ? "encendida" : "apagada")}: no se ha cambiado nada."
                : cambio.Encendido
                    ? $"Encendida en {nombre}: en el picking, las muestras con código hay que leerlas (su etiqueta del hueco o el cajón)."
                    : $"Apagada en {nombre}: las muestras se pueden volver a teclear en el picking, como antes.";
            return resultado;
        }

        private async Task Contar(string empresa, string almacen, EstadoLecturaObligatoriaMuestrasDTO estado)
        {
            List<MuestraAlmacen> conStock = ((await muestras.LeerMuestras(empresa, almacen).ConfigureAwait(false)) ?? new List<MuestraAlmacen>())
                .Where(ReglasMuestras.EsActiva)
                .Where(m => m.Stock > 0)
                .GroupBy(m => m.Producto.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .ToList();
            estado.MuestrasConCodigo = conStock.Count(ReglasMuestras.TieneCodigo);
            estado.MuestrasSinCodigo = conStock.Count - estado.MuestrasConCodigo;
        }
    }

    /// <summary>POST api/Almacen/Recogidas/{tipo}/{numero}/MuestraSinEtiqueta: la etiqueta de la muestra no está en su hueco o no se lee.</summary>
    public class MuestraSinEtiquetaSalidaDTO
    {
        public string Producto { get; set; }
        /// <summary>El hueco de la parada (opcional: sin él, el de la primera parada de ese producto).</summary>
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>NoLleva | NoSeLee | Otro (este con <see cref="Comentario"/>), como «Sin etiqueta» al recibir.</summary>
        public string Motivo { get; set; }
        public string Comentario { get; set; }
        public string Dispositivo { get; set; }
    }

    public class ResultadoMuestraSinEtiquetaSalidaDTO
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        /// <summary>«001/002/004», o null si la parada no tiene hueco (las tiendas).</summary>
        public string Hueco { get; set; }
        /// <summary>Se ha mandado el aviso a Almacén ahora.</summary>
        public bool Avisado { get; set; }
        /// <summary>Ya se avisó hoy de este producto en este hueco: no se repite.</summary>
        public bool YaAvisadoHoy { get; set; }
        /// <summary>Para enseñarlo tal cual. La unidad la cuenta la app (tecleada), como cualquier otra.</summary>
        public string Mensaje { get; set; }
    }

    public interface IServicioMuestraSinEtiquetaSalida
    {
        /// <returns>Null si no existe la recogida.</returns>
        /// <exception cref="ArgumentException">Tipo, producto o motivo que no valen, o la línea no exige leer: el motivo es para el usuario.</exception>
        Task<ResultadoMuestraSinEtiquetaSalidaDTO> Apuntar(string empresa, string tipo, int numero, MuestraSinEtiquetaSalidaDTO peticion, string usuario);
    }

    /// <summary>
    /// La vía de escape de la lectura obligatoria: la etiqueta de la muestra no está en el hueco (o el cajón) o no se lee. Se permite
    /// contar a mano (la app teclea la unidad, como siempre), NO se avisa a Compras (no es el código del fabricante) y se avisa a
    /// ALMACÉN (campana de Nesto, tipo <see cref="AvisadorMuestraSinEtiqueta.TIPO_NOTIFICACION"/>) para que la reimpriman y la peguen;
    /// un aviso por almacén + producto + hueco y día como mucho (en memoria: un reinicio puede repetir uno).
    /// </summary>
    public class ServicioMuestraSinEtiquetaSalida : IServicioMuestraSinEtiquetaSalida
    {
        private readonly IServicioPreparacionAlmacen salidas;
        private readonly IInterruptorLecturaMuestras interruptor;
        private readonly IAvisadorMuestraSinEtiqueta avisador;
        private readonly MemoriaAvisosMuestraSinEtiqueta memoria;
        private readonly Func<DateTime> ahora;

        public ServicioMuestraSinEtiquetaSalida(IServicioPreparacionAlmacen salidas, IInterruptorLecturaMuestras interruptor,
            IAvisadorMuestraSinEtiqueta avisador, MemoriaAvisosMuestraSinEtiqueta memoria, Func<DateTime> ahora = null)
        {
            this.salidas = salidas ?? throw new ArgumentNullException(nameof(salidas));
            this.interruptor = interruptor ?? throw new ArgumentNullException(nameof(interruptor));
            this.avisador = avisador ?? throw new ArgumentNullException(nameof(avisador));
            this.memoria = memoria ?? throw new ArgumentNullException(nameof(memoria));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<ResultadoMuestraSinEtiquetaSalidaDTO> Apuntar(string empresa, string tipo, int numero, MuestraSinEtiquetaSalidaDTO peticion, string usuario)
        {
            string tipoSalida = CasadorEscaneos.NormalizarTipoOrigen(tipo) ?? throw new ArgumentException("El tipo de recogida tiene que ser PICK o REPO.");
            string producto = peticion?.Producto?.Trim();
            if (string.IsNullOrEmpty(producto))
            {
                throw new ArgumentException("Falta el producto.");
            }
            string motivo = ServicioSinEtiquetaRecepcion.NormalizarMotivo(peticion.Motivo)
                ?? throw new ArgumentException("El motivo tiene que ser NoLleva, NoSeLee u Otro.");
            if (motivo == ServicioSinEtiquetaRecepcion.MOTIVO_OTRO && string.IsNullOrWhiteSpace(peticion.Comentario))
            {
                throw new ArgumentException("Con «Otro» hay que escribir qué pasa.");
            }

            RecogidaAlmacenDTO recogida = await salidas.LeerRecogida(empresa, tipoSalida, numero).ConfigureAwait(false);
            if (recogida == null)
            {
                return null;
            }
            List<LineaRecogidaDTO> delProducto = (recogida.Lineas ?? new List<LineaRecogidaDTO>())
                .Where(l => string.Equals(l.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                .ToList();
            string huecoPedido = CasadorEscaneos.TextoUbicacion(peticion.Pasillo, peticion.Fila, peticion.Columna);
            LineaRecogidaDTO linea = huecoPedido == null
                ? delProducto.FirstOrDefault()
                : delProducto.FirstOrDefault(l => string.Equals(l.Ubicacion, huecoPedido, StringComparison.OrdinalIgnoreCase));
            if (linea == null)
            {
                throw new ArgumentException(huecoPedido == null
                    ? $"El producto {producto} no está en esta recogida."
                    : $"El producto {producto} no tiene ninguna parada en el hueco {huecoPedido} de esta recogida.");
            }
            if (!MuestrasSinCodigo.EsMuestra(linea.SubGrupoProducto))
            {
                throw new ArgumentException($"El {producto} no es una muestra: si no se lee su código, teclee su referencia como siempre.");
            }
            string almacen = recogida.Almacen?.Trim().ToUpperInvariant();
            if (!await interruptor.Encendido(almacen).ConfigureAwait(false))
            {
                throw new ArgumentException($"En {ReglaLecturaObligatoriaMuestras.NombreAlmacen(almacen) ?? "este almacén"} no es obligatorio leer la etiqueta de las muestras: teclee su referencia como siempre.");
            }

            string hueco = linea.Ubicacion;
            string nombre = linea.Descripcion?.Trim();
            string producto2 = linea.Producto.Trim();
            var resultado = new ResultadoMuestraSinEtiquetaSalidaDTO { Producto = producto2, Almacen = almacen, Hueco = hueco };
            string dondeTexto = hueco == null ? "en su cajón" : $"en el hueco {hueco}";
            string clave = MemoriaAvisosMuestraSinEtiqueta.Clave(almacen, producto2, hueco);
            DateTime hoy = ahora().Date;
            if (memoria.YaAvisado(clave, hoy))
            {
                resultado.YaAvisadoHoy = true;
                resultado.Mensaje = $"Cuente la muestra a mano. Almacén ya está avisado hoy de la etiqueta del {producto2} {dondeTexto}.";
                return resultado;
            }
            bool avisado = await avisador.Avisar(empresa, almacen, producto2, nombre, hueco, usuario,
                ServicioSinEtiquetaRecepcion.TextoMotivo(motivo, peticion.Comentario), tipoSalida, numero).ConfigureAwait(false);
            if (avisado)
            {
                memoria.Apuntar(clave, hoy);
            }
            resultado.Avisado = avisado;
            resultado.Mensaje = avisado
                ? $"Cuente la muestra a mano. Se ha avisado a Almacén para que reimprima y pegue la etiqueta del {producto2} {dondeTexto}."
                : $"Cuente la muestra a mano. No se ha podido avisar a Almacén: dígales que reimpriman la etiqueta del {producto2} {dondeTexto}.";
            return resultado;
        }
    }

    /// <summary>Qué se ha avisado hoy (almacén + producto + hueco). Singleton; se olvida de los días anteriores.</summary>
    public class MemoriaAvisosMuestraSinEtiqueta
    {
        private readonly ConcurrentDictionary<string, DateTime> avisados = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public static string Clave(string almacen, string producto, string hueco)
            => string.Join("|", almacen?.Trim().ToUpperInvariant(), producto?.Trim().ToUpperInvariant(), hueco?.Trim() ?? string.Empty);

        public bool YaAvisado(string clave, DateTime dia) => avisados.TryGetValue(clave, out DateTime cuando) && cuando == dia.Date;

        public void Apuntar(string clave, DateTime dia)
        {
            foreach (KeyValuePair<string, DateTime> viejo in avisados.Where(a => a.Value != dia.Date).ToList())
            {
                _ = avisados.TryRemove(viejo.Key, out _);
            }
            avisados[clave] = dia.Date;
        }
    }

    public interface IAvisadorMuestraSinEtiqueta
    {
        /// <returns>false si no había a quién avisar.</returns>
        Task<bool> Avisar(string empresa, string almacen, string producto, string nombre, string hueco, string usuario, string motivo, string tipo, int numero);
    }

    /// <summary>
    /// La campana de Nesto al grupo Almacén (y, si la recogida es de una tienda, a su gente, que es quien pega la etiqueta del cajón).
    /// Nunca a Compras: la etiqueta es nuestra, no el código del fabricante.
    /// </summary>
    public class AvisadorMuestraSinEtiqueta : IAvisadorMuestraSinEtiqueta
    {
        public const string TIPO_NOTIFICACION = "MuestraSinEtiquetaEnHueco";
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<string, List<string>> miembrosDelGrupo;
        private readonly Func<string, string, Task<List<string>>> usuariosDeLaTienda;

        public AvisadorMuestraSinEtiqueta(IServicioNotificacionesPush notificaciones) : this(notificaciones, null, null)
        {
        }

        internal AvisadorMuestraSinEtiqueta(IServicioNotificacionesPush notificaciones, Func<string, List<string>> miembrosDelGrupo,
            Func<string, string, Task<List<string>>> usuariosDeLaTienda)
        {
            this.notificaciones = notificaciones ?? throw new ArgumentNullException(nameof(notificaciones));
            this.miembrosDelGrupo = miembrosDelGrupo ?? MiembrosGrupoDominio.Leer;
            this.usuariosDeLaTienda = usuariosDeLaTienda ?? LeerUsuariosDeLaTienda;
        }

        private static async Task<List<string>> LeerUsuariosDeLaTienda(string empresa, string almacen)
        {
            using (var db = new NVEntities())
            {
                return await Reposiciones.AvisadorReposicionAutomatica.UsuariosDelAlmacen(db, empresa, almacen).ConfigureAwait(false);
            }
        }

        /// <summary>El texto del aviso (puro).</summary>
        internal static string Cuerpo(string almacen, string producto, string nombre, string hueco, string usuario, string motivo, string tipo, int numero)
        {
            string quien = usuario?.Trim();
            quien = string.IsNullOrEmpty(quien) ? "Un mozo" : quien.Substring(quien.LastIndexOf('\\') + 1);
            string donde = hueco == null ? $"en el cajón de {ReglaLecturaObligatoriaMuestras.NombreAlmacen(almacen)}" : $"en el hueco {hueco} de {ReglaLecturaObligatoriaMuestras.NombreAlmacen(almacen)}";
            string documento = tipo == CasadorEscaneos.ORIGEN_REPOSICION ? $"la reposición {numero}" : $"el picking {numero}";
            return $"{quien} no ha podido leer la etiqueta de la muestra {producto}{(string.IsNullOrWhiteSpace(nombre) ? string.Empty : " (" + nombre.Trim() + ")")} {donde} ({motivo}) en {documento} y la ha contado a mano. Reimprima la etiqueta y péguela.";
        }

        public async Task<bool> Avisar(string empresa, string almacen, string producto, string nombre, string hueco, string usuario, string motivo, string tipo, int numero)
        {
            var candidatos = new List<string>(miembrosDelGrupo(Constantes.GruposSeguridad.ALMACEN) ?? new List<string>());
            string tienda = almacen?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(tienda) && tienda != Constantes.Almacenes.ALGETE)
            {
                try
                {
                    candidatos.AddRange(await usuariosDeLaTienda(empresa?.Trim(), tienda).ConfigureAwait(false) ?? new List<string>());
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Muestra sin etiqueta] No se ha podido leer la gente de {tienda}: {ex.Message}", ex));
                }
            }
            List<string> usuarios = candidatos
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!usuarios.Any())
            {
                ElmahHelper.Log(new Exception($"[Muestra sin etiqueta] No hay a quién avisar: {producto} en {almacen} {hueco}"));
                return false;
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = $"Etiqueta de la muestra {producto} {(hueco == null ? "en el cajón" : "en el hueco " + hueco)}",
                Cuerpo = Cuerpo(almacen, producto, nombre, hueco, usuario, motivo, tipo, numero),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["producto"] = producto,
                    ["hueco"] = hueco ?? string.Empty,
                    ["almacen"] = almacen ?? string.Empty,
                    ["usuario"] = usuario?.Trim() ?? string.Empty
                }
            };
            foreach (string destinatario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(destinatario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            return true;
        }
    }
}
