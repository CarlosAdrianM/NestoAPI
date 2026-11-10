using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PreparacionAlmacen;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 5, Ariadna#13): la etiqueta de agencia desde el packing de Ariadna en UNA llamada
    /// (POST api/Almacen/Pedidos/{pedido}/Etiqueta). Junta lo que Agencias de Nesto hace con tres botones:
    /// <list type="number">
    /// <item>Grabar el envío desde el pedido (slice 2, <see cref="IServicioEnvioDesdePedido"/>) con los bultos de la entrega
    /// en el packing (cajas distintas). Si ya hay un envío en curso del pedido (un intento anterior), no se graba otro.</item>
    /// <item>Registrarlo en la agencia (/Tramitar) si su flujo es «registrar al imprimir» (CTT, Innovatrans y, desde
    /// NestoAPI#803, el reparto propio: albarán RP{envío}, etiquetas de bulto RP{envío}-{N} y Tramitado en el acto) y aún no
    /// tiene albarán.</item>
    /// <item>Imprimir la etiqueta en el servidor (/ImprimirEtiqueta, slice 4), en la impresora pedida o en la del usuario.</item>
    /// <item>Facturar el pedido por el camino de rutas (#592) si el usuario tiene «FacturarAlImprimirEtiqueta».</item>
    /// </list>
    /// GLS (hasta #552) y Canteras: se graba el envío y se dice que la etiqueta hay que sacarla en Nesto.
    /// Si falla registrar o imprimir, el envío queda grabado (y, si la agencia dio albarán, registrado): repetir la llamada
    /// sigue desde donde se quedó, sin duplicar nada.
    /// </summary>
    public interface IServicioEtiquetaPacking
    {
        Task<ResultadoEtiquetaPacking> Imprimir(int pedido, PeticionEtiquetaPackingDTO peticion, IPrincipal usuario);

        /// <summary>NestoAPI#828 (S3/S6): vuelve a imprimir el papel pendiente del packing del pedido y de su entrega, sin facturar.</summary>
        Task<ResultadoDocumentosEnPapel> ReintentarDocumentosEnPapel(int pedido, string empresa, IPrincipal usuario);
    }

    public enum TipoResultadoEtiquetaPacking
    {
        /// <summary>200.</summary>
        Hecha,
        /// <summary>409: lo pendiente de DesdePedido (confirmar, elegir los prepagos, las dimensiones).</summary>
        Conflicto,
        /// <summary>400.</summary>
        Invalida,
        /// <summary>404: el pedido no existe.</summary>
        NoEncontrado,
        /// <summary>502: el envío está grabado, pero la agencia o la impresora han fallado.</summary>
        FalloAgencia
    }

    public class ResultadoEtiquetaPacking
    {
        public TipoResultadoEtiquetaPacking Tipo { get; private set; }
        public EtiquetaPackingDTO Etiqueta { get; private set; }
        public ConflictoEnvioDesdePedidoDTO Conflicto { get; private set; }
        public ErrorEnvioDesdePedidoDTO Error { get; private set; }

        public static ResultadoEtiquetaPacking Hecha(EtiquetaPackingDTO etiqueta)
            => new ResultadoEtiquetaPacking { Tipo = TipoResultadoEtiquetaPacking.Hecha, Etiqueta = etiqueta };

        public static ResultadoEtiquetaPacking Fallo(EtiquetaPackingDTO etiqueta)
            => new ResultadoEtiquetaPacking { Tipo = TipoResultadoEtiquetaPacking.FalloAgencia, Etiqueta = etiqueta };

        public static ResultadoEtiquetaPacking ConPendientes(ConflictoEnvioDesdePedidoDTO conflicto)
            => new ResultadoEtiquetaPacking { Tipo = TipoResultadoEtiquetaPacking.Conflicto, Conflicto = conflicto };

        public static ResultadoEtiquetaPacking Invalida(ErrorEnvioDesdePedidoDTO error)
            => new ResultadoEtiquetaPacking { Tipo = TipoResultadoEtiquetaPacking.Invalida, Error = error };

        public static ResultadoEtiquetaPacking Invalida(string codigo, string mensaje)
            => Invalida(new ErrorEnvioDesdePedidoDTO { Codigo = codigo, Message = mensaje });

        public static ResultadoEtiquetaPacking NoEncontrado()
            => new ResultadoEtiquetaPacking { Tipo = TipoResultadoEtiquetaPacking.NoEncontrado };
    }

    /// <summary>Las cajas de la entrega del pedido en el packing de Ariadna.</summary>
    public class EntregaParaEtiqueta
    {
        /// <summary>NestoAPI#771: el aviso de la etiqueta cuando la entrega no tiene ninguna foto del packing.</summary>
        public const string AVISO_SIN_FOTOS_TEXTO = "La entrega no tiene ninguna foto del packing.";

        /// <summary>Cajas distintas (una caja compartida por dos pedidos cuenta una vez).</summary>
        public int Bultos { get; set; }
        public int BultosSinFoto { get; set; }
        /// <summary>
        /// NestoAPI#771: fotos distintas de la entrega (las de las cajas, como antes, y las libres del packing). La regla nueva es
        /// mínimo una por entrega, no una por caja. Null si no se sabe (sin packing en curso solo se ven las de las cajas).
        /// </summary>
        public int? Fotos { get; set; }
        /// <summary>Los demás pedidos de la misma entrega (mismo cliente y dirección en el picking).</summary>
        public List<int> OtrosPedidos { get; set; } = new List<int>();

        /// <summary>
        /// La entrega de <paramref name="pedido"/> en el packing de su picking. Una caja es su foto (cada pedido de una caja
        /// compartida tiene su fila con la misma foto) o, sin foto, su número en la entrega.
        /// </summary>
        public static EntregaParaEtiqueta De(PackingAlmacenDTO packing, int pedido)
        {
            EntregaPackingAlmacenDTO entrega = packing?.Entregas?.FirstOrDefault(e => e.Pedidos != null && e.Pedidos.Any(p => p.Pedido == pedido));
            if (entrega == null)
            {
                return null;
            }
            List<BultoAlmacenDTO> bultos = entrega.Bultos ?? new List<BultoAlmacenDTO>();
            List<IGrouping<string, BultoAlmacenDTO>> cajas = bultos.GroupBy(ClaveCaja).ToList();
            int fotosLibres = (entrega.Fotos ?? new List<FotoPackingAlmacenDTO>())
                .Where(f => f != null).Select(f => string.IsNullOrWhiteSpace(f.RutaBlob) ? "id:" + f.Id : f.RutaBlob.Trim()).Distinct().Count();
            return new EntregaParaEtiqueta
            {
                Bultos = cajas.Count,
                BultosSinFoto = cajas.Count(c => !c.Any(b => b.TieneFoto)),
                Fotos = cajas.Count(c => c.Any(b => b.TieneFoto)) + fotosLibres,
                OtrosPedidos = entrega.Pedidos.Select(p => p.Pedido).Where(p => p != pedido).Distinct().OrderBy(p => p).ToList()
            };
        }

        /// <summary>Sin packing en curso: los bultos del pedido (GET Almacen/Pedidos/{pedido}/Bultos), por caja.</summary>
        public static EntregaParaEtiqueta De(IEnumerable<BultoAlmacenDTO> bultosDelPedido)
        {
            List<IGrouping<string, BultoAlmacenDTO>> cajas = (bultosDelPedido ?? Enumerable.Empty<BultoAlmacenDTO>()).GroupBy(ClaveCaja).ToList();
            int conFoto = cajas.Count(c => c.Any(b => b.TieneFoto));
            return new EntregaParaEtiqueta
            {
                Bultos = cajas.Count,
                BultosSinFoto = cajas.Count(c => !c.Any(b => b.TieneFoto)),
                // Aquí no se ven las fotos libres del packing: sin ninguna de caja no se sabe si hay (no se avisa)
                Fotos = conFoto > 0 ? conFoto : (int?)null
            };
        }

        private static string ClaveCaja(BultoAlmacenDTO bulto)
            => !string.IsNullOrWhiteSpace(bulto.RutaBlob) ? "foto:" + bulto.RutaBlob.Trim() : $"bulto:{bulto.Picking}:{bulto.Bulto}";
    }

    /// <summary>Lo que ha pasado en un paso contra la agencia o la impresora (Tramitar, ImprimirEtiqueta).</summary>
    public class PasoEtiquetaAgencia
    {
        public bool Exito { get; set; }
        public HttpStatusCode Estado { get; set; }
        /// <summary>El motivo del fallo, tal como lo da el endpoint.</summary>
        public string Mensaje { get; set; }
        public string CodigoBarras { get; set; }
        public int? Bultos { get; set; }
        public string Impresora { get; set; }

        public static PasoEtiquetaAgencia Bien(string codigoBarras = null, int? bultos = null, string impresora = null)
            => new PasoEtiquetaAgencia { Exito = true, Estado = HttpStatusCode.OK, CodigoBarras = codigoBarras, Bultos = bultos, Impresora = impresora };

        public static PasoEtiquetaAgencia Mal(HttpStatusCode estado, string mensaje)
            => new PasoEtiquetaAgencia { Exito = false, Estado = estado, Mensaje = mensaje };
    }

    /// <summary>
    /// Los pasos de la etiqueta del packing, detrás de una interfaz para probar el orden y los fallos sin BD ni agencias. La
    /// implementación real (<see cref="PasosEtiquetaPackingApi"/>) llama a los mismos endpoints que Agencias de Nesto.
    /// </summary>
    /// <remarks>NestoAPI#828: extiende los pasos del papel del packing (facturar, la ruta, las marcas, regenerar).</remarks>
    public interface IPasosEtiquetaPacking : IPasosDocumentosEnPapel, IDisposable
    {
        /// <summary>
        /// El último envío EN CURSO (Estado 0) del pedido, como el «envío actual» de Agencias; null si no hay. NestoAPI#803: o
        /// el del reparto propio de hoy ya registrado (Tramitado con su albarán RP), que se reimprime.
        /// </summary>
        Task<EnviosAgencia> LeerEnvioEnCurso(string empresa, int pedido);
        /// <summary>Las cajas de la entrega en el packing; null si el pedido no tiene packing ni bultos.</summary>
        Task<EntregaParaEtiqueta> LeerEntrega(string empresa, int pedido);
        Task<ResultadoEnvioDesdePedido> CrearDesdePedido(CrearEnvioDesdePedidoDTO peticion, string usuario);
        /// <summary>POST api/EnviosAgencias/{id}/Tramitar.</summary>
        Task<PasoEtiquetaAgencia> Tramitar(int envio);
        /// <summary>POST api/EnviosAgencias/{id}/ImprimirEtiqueta (impresora null = la del usuario).</summary>
        Task<PasoEtiquetaAgencia> Imprimir(int envio, string impresora);
        Task<string> NombreAgencia(int agencia);
    }

    public class ServicioEtiquetaPacking : IServicioEtiquetaPacking
    {
        /// <summary>El parámetro de usuario de «Facturar al imprimir etiqueta» (en Nesto es la casilla de Agencias).</summary>
        public const string PARAMETRO_FACTURAR = "FacturarAlImprimirEtiqueta";
        /// <summary>NestoAPI#740: tope de los bultos tecleados (un error de tecleo, no un envío real).</summary>
        public const int MAXIMO_BULTOS = 99;
        internal const string TIPO_ERROR_SIN_LINEAS = "Sin líneas para facturar";

        private readonly Func<IPrincipal, IPasosEtiquetaPacking> crearPasos;
        private readonly ILectorParametrosUsuario parametros;
        private readonly Action<Exception> registrar;

        /// <summary>NestoAPI#552 (fase 4): el interruptor Agencias:GlsTramitarPorApi. Sustituible en los tests.</summary>
        internal Func<bool> GlsEtiquetaPorApi { get; set; } = Gls.InterruptorTramitacionGls.EstaActivo;

        /// <summary>
        /// NestoAPI#698/#734 (29/10/26, hallado al hacer Ariadna#60): un pedido retenido (también el que espera la respuesta de
        /// Administración por un enlace de pago pendiente) no saca etiqueta desde el packing, igual que no sale por
        /// POST api/EnviosAgencias/DesdePedido. (empresa, pedido) → motivo o null. Sustituible en los tests.
        /// </summary>
        /// <remarks>
        /// NestoAPI#831: (empresa, pedido, etiqueta del reparto propio) → motivo o null. Con true, un aplazado (picking con fecha
        /// futura) no bloquea: la etiqueta del reparto propio sale con su día de reparto. Un retenido (modo 5) bloquea siempre.
        /// </remarks>
        internal Func<string, int, bool, Task<string>> MotivoPedidoRetenido { get; set; } = PedidosVenta.Retencion.GuardiaPedidoRetenido.Motivo;

        /// <summary>NestoAPI#831: hoy, para saber si el reparto es de otro día. Sustituible en los tests.</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        /// <summary>NestoAPI#828: quién imprime el papel del packing (Ariadna: el servidor). Sustituible en los tests.</summary>
        internal IImpresoraPapelPacking ImpresoraPapel { get; set; } = new ImpresoraPapelPackingServidor();

        /// <summary>NestoAPI#828: la ruta → su tipo (null = TipoRutaFactory). Sustituible en los tests.</summary>
        internal Func<string, ITipoRuta> TipoDeRuta { get; set; }

        public ServicioEtiquetaPacking(Func<IPrincipal, IPasosEtiquetaPacking> crearPasos, ILectorParametrosUsuario parametros,
            Action<Exception> registrar = null)
        {
            this.crearPasos = crearPasos ?? throw new ArgumentNullException(nameof(crearPasos));
            this.parametros = parametros;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        // Sin ConfigureAwait(false): ELMAH toma el usuario de HttpContext.Current.
        public async Task<ResultadoEtiquetaPacking> Imprimir(int pedido, PeticionEtiquetaPackingDTO peticion, IPrincipal usuario)
        {
            peticion = peticion ?? new PeticionEtiquetaPackingDTO();
            if (pedido <= 0)
            {
                return ResultadoEtiquetaPacking.Invalida(CodigosErrorEnvioDesdePedido.PETICION_INVALIDA, "Hay que indicar el pedido.");
            }
            if (peticion.Bultos.HasValue && (peticion.Bultos.Value < 0 || peticion.Bultos.Value > MAXIMO_BULTOS))
            {
                return ResultadoEtiquetaPacking.Invalida(CodigosErrorEnvioDesdePedido.PETICION_INVALIDA,
                    $"Los bultos tienen que estar entre 1 y {MAXIMO_BULTOS}.");
            }
            if (peticion.Reembolso.HasValue && peticion.Reembolso.Value < 0m)
            {
                return ResultadoEtiquetaPacking.Invalida(CodigosErrorEnvioDesdePedido.PETICION_INVALIDA, "El reembolso no puede ser negativo.");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string usuarioAuditoria = UsuarioAuditoriaHelper.Resolver(usuario, "NestoAPI");

            // NestoAPI#831: un retenido (modo 5) no saca etiqueta; un aplazado, solo la del reparto propio (que no se carga hasta
            // su día). Como la agencia aún no se sabe, el aplazado se mira cuando se tiene el envío.
            string aplazado = await MotivoPedidoRetenido(empresa, pedido, false);
            if (aplazado != null)
            {
                string retenido = await MotivoPedidoRetenido(empresa, pedido, true);
                if (retenido != null)
                {
                    return ResultadoEtiquetaPacking.Invalida(CodigosEtiquetaPacking.PEDIDO_RETENIDO, retenido);
                }
            }

            using (IPasosEtiquetaPacking pasos = crearPasos(usuario))
            {
                var etiqueta = new EtiquetaPackingDTO { Pedido = pedido };

                // 1. El envío: el de un intento anterior o uno nuevo con las cajas de la entrega.
                EnviosAgencia envio = await pasos.LeerEnvioEnCurso(empresa, pedido);
                EntregaParaEtiqueta entrega = null;
                if (envio == null)
                {
                    entrega = await pasos.LeerEntrega(empresa, pedido);
                    // NestoAPI#740: los bultos tecleados al imprimir mandan sobre las cajas cerradas (y no exigen ninguna)
                    bool bultosTecleados = peticion.Bultos.HasValue && peticion.Bultos.Value > 0;
                    int bultos = bultosTecleados ? peticion.Bultos.Value : entrega?.Bultos ?? 0;
                    if (bultos <= 0)
                    {
                        return ResultadoEtiquetaPacking.Invalida(CodigosEtiquetaPacking.SIN_BULTOS,
                            $"El pedido {pedido} no tiene ninguna caja cerrada en el packing. Indique los bultos o cierre antes las cajas con su foto.");
                    }

                    ResultadoEnvioDesdePedido creado = await pasos.CrearDesdePedido(new CrearEnvioDesdePedidoDTO
                    {
                        Empresa = empresa,
                        Pedido = pedido,
                        Bultos = (short)Math.Min(bultos, short.MaxValue),
                        Peso = peticion.Peso,
                        // La ampliación no se confirma desde aquí: ver más abajo
                        Confirmaciones = (peticion.Confirmaciones ?? new List<string>())
                            .Where(c => !string.Equals(c?.Trim(), CodigosAvisoPropuestaEnvio.AMPLIACION, StringComparison.OrdinalIgnoreCase)).ToList(),
                        DescontarPrepagos = peticion.DescontarPrepagos,
                        Dimensiones = peticion.Dimensiones,
                        // NestoAPI#765: el reembolso del packing (en una nota de entrega lo marca el mozo); sin él, el de la propuesta
                        Cambios = peticion.Reembolso.HasValue ? new CambiosEnvioDesdePedidoDTO { Reembolso = peticion.Reembolso.Value } : null
                    }, usuarioAuditoria);

                    switch (creado.Tipo)
                    {
                        case TipoResultadoEnvioDesdePedido.Conflicto:
                            AvisoPropuestaEnvio ampliacion = creado.Conflicto?.Pendientes?.FirstOrDefault(a =>
                                string.Equals(a?.Codigo, CodigosAvisoPropuestaEnvio.AMPLIACION, StringComparison.OrdinalIgnoreCase));
                            if (ampliacion != null)
                            {
                                // En una ampliación los bultos son los del envío ENTERO (los de antes más estos) y el packing
                                // solo sabe los de esta entrega: si se confirmara, el envío se quedaría con menos bultos.
                                return ResultadoEtiquetaPacking.Invalida(CodigosEtiquetaPacking.AMPLIACION,
                                    $"{ampliacion.Texto?.Trim()} Haga la etiqueta desde Agencias de Nesto, con los bultos de todo el envío.");
                            }
                            return ResultadoEtiquetaPacking.ConPendientes(creado.Conflicto);
                        case TipoResultadoEnvioDesdePedido.NoEncontrado:
                            return ResultadoEtiquetaPacking.NoEncontrado();
                        case TipoResultadoEnvioDesdePedido.Invalido:
                            return ResultadoEtiquetaPacking.Invalida(creado.Error);
                        case TipoResultadoEnvioDesdePedido.RepartoPropioRepetido:
                            // NestoAPI#847: LeerEnvioEnCurso ya lo reimprime (#803); solo llega aquí si lo han creado entretanto.
                            return ResultadoEtiquetaPacking.Invalida(creado.Repetido.Codigo, creado.Repetido.Message);
                    }

                    envio = creado.Envio.Envio;
                    etiqueta.EnvioNuevo = true;
                    etiqueta.Avisos.AddRange(creado.Envio.Avisos ?? new List<string>());
                    // Con los bultos tecleados no se cierran cajas: el aviso de las fotos no tiene sentido. NestoAPI#771: ya no hace
                    // falta una foto por caja (mínimo una por entrega): solo se avisa si la entrega no tiene ninguna.
                    if (!bultosTecleados && entrega?.Bultos > 0 && entrega.Fotos == 0)
                    {
                        etiqueta.Avisos.Add(EntregaParaEtiqueta.AVISO_SIN_FOTOS_TEXTO);
                    }
                    if (entrega?.OtrosPedidos?.Count > 0)
                    {
                        etiqueta.Avisos.Add($"En la misma entrega van también {(entrega.OtrosPedidos.Count == 1 ? "el pedido" : "los pedidos")} " +
                            $"{string.Join(", ", entrega.OtrosPedidos)}: esta etiqueta es para todas las cajas, pero el envío es del pedido {pedido}. " +
                            "Si hay que añadirlos al envío, hágalo desde Agencias de Nesto.");
                    }
                }

                // NestoAPI#831: aplazado y no es del reparto propio: no sale (el envío nuevo ya lo para DesdePedido)
                if (aplazado != null && envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
                {
                    return ResultadoEtiquetaPacking.Invalida(CodigosEtiquetaPacking.PEDIDO_RETENIDO, aplazado);
                }

                etiqueta.Envio = envio.Numero;
                etiqueta.Agencia = envio.Agencia;
                etiqueta.Bultos = envio.Bultos;
                etiqueta.CodigoBarras = string.IsNullOrWhiteSpace(envio.CodigoBarras) ? null : envio.CodigoBarras.Trim();
                etiqueta.AgenciaNombre = await NombreSeguro(pasos, envio.Agencia);
                string agencia = string.IsNullOrWhiteSpace(etiqueta.AgenciaNombre) ? $"la agencia {envio.Agencia}" : etiqueta.AgenciaNombre;

                // 2. GLS (hasta #552) y Canteras: el envío está grabado y la etiqueta sigue saliendo de Nesto.
                // NestoAPI#552 (fase 4): con Agencias:GlsTramitarPorApi encendido, la etiqueta de GLS (agencia 1) la monta e
                // imprime la API sin grabar en GLS (se tramita al cerrar el día, como siempre). Fase 5: también la 5 (empresa espejo).
                bool glsPorApi = Gls.AgenciasGls.Es(envio.Agencia) && GlsEtiquetaPorApi();
                if (!glsPorApi && ServicioEnvioDesdePedido.FlujoDe(envio.Agencia) != FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR)
                {
                    etiqueta.EtiquetaEnNesto = true;
                    etiqueta.Message = $"La etiqueta de {agencia} hay que sacarla en Nesto.";
                    etiqueta.Avisos.Insert(0, etiqueta.EnvioNuevo
                        ? $"El envío {envio.Numero} está grabado ({TextoBultos(envio.Bultos)}): imprima la etiqueta desde Agencias de Nesto."
                        : $"El envío {envio.Numero} ya estaba grabado ({TextoBultos(envio.Bultos)}): imprima la etiqueta desde Agencias de Nesto.");
                    return ResultadoEtiquetaPacking.Hecha(etiqueta);
                }

                // 3. CTT e Innovatrans: registrar el envío en la agencia si aún no tiene albarán. GLS no se registra aquí: su
                // código de barras nace con el envío y se graba en GLS con «Tramitar todos».
                etiqueta.Reimpresion = glsPorApi ? !etiqueta.EnvioNuevo : etiqueta.CodigoBarras != null;
                if (!glsPorApi && !etiqueta.Reimpresion)
                {
                    PasoEtiquetaAgencia tramitado = await pasos.Tramitar(envio.Numero);
                    if (!tramitado.Exito)
                    {
                        etiqueta.Codigo = CodigosEtiquetaPacking.TRAMITAR_FALLIDO;
                        etiqueta.Message = $"{agencia} no ha registrado el envío {envio.Numero}: {Motivo(tramitado)} " +
                            "El envío está grabado: vuelva a pedir la etiqueta y seguirá desde aquí, sin duplicarlo.";
                        return ResultadoEtiquetaPacking.Fallo(etiqueta);
                    }
                    etiqueta.CodigoBarras = tramitado.CodigoBarras ?? etiqueta.CodigoBarras;
                    if (tramitado.Bultos.HasValue && tramitado.Bultos.Value > 0)
                    {
                        etiqueta.Bultos = tramitado.Bultos.Value;
                    }
                }

                // 4. La etiqueta, en la impresora.
                PasoEtiquetaAgencia impreso = await pasos.Imprimir(envio.Numero, string.IsNullOrWhiteSpace(peticion.Impresora) ? null : peticion.Impresora.Trim());
                if (!impreso.Exito)
                {
                    etiqueta.Codigo = CodigosEtiquetaPacking.IMPRIMIR_FALLIDO;
                    etiqueta.Impresora = impreso.Impresora ?? peticion.Impresora;
                    etiqueta.Message = $"No se ha impreso la etiqueta de {agencia}: {Motivo(impreso)} " +
                        $"El envío {envio.Numero} está {(etiqueta.CodigoBarras != null ? $"registrado en {agencia} (albarán {etiqueta.CodigoBarras})" : "grabado")}: " +
                        "vuelva a pedir la etiqueta para imprimirla otra vez, sin duplicar el envío.";
                    return ResultadoEtiquetaPacking.Fallo(etiqueta);
                }
                etiqueta.Impresa = true;
                etiqueta.Impresora = impreso.Impresora;
                etiqueta.CodigoBarras = impreso.CodigoBarras ?? etiqueta.CodigoBarras;
                etiqueta.Message = $"Etiqueta de {agencia} {(etiqueta.Reimpresion ? "reimpresa" : "impresa")}" +
                    $"{(string.IsNullOrWhiteSpace(etiqueta.Impresora) ? string.Empty : " en " + etiqueta.Impresora.Trim())} ({TextoBultos(etiqueta.Bultos)}).";

                // 5. Facturar al imprimir (#592), si el usuario lo tiene: lo que diga no deshace la etiqueta.
                // NestoAPI#765: menos las notas de reparto propio, que las procesa Facturar rutas (si se procesaran aquí, Facturar
                // rutas ya no las vería y no saldría el documento NE: Ariadna no imprime documentos).
                // NestoAPI#828: con el núcleo del papel del packing (DocumentosEnPapelDelEnvio): todos los pedidos de la entrega, la
                // factura física impresa (para meterla en la última caja) y, si la impresora falla, pendiente para reimprimir.
                if (FacturarAlImprimir(usuarioAuditoria))
                {
                    // NestoAPI#831: el del reparto propio de otro día no se factura ahora (el albarán no se puede hasta esa fecha):
                    // lo hace Facturar rutas el día del reparto.
                    if (envio.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                        && (aplazado != null || RepartoPropio.FechaRepartoPropio.EsDeOtroDia(envio, Hoy())))
                    {
                        string dia = RepartoPropio.FechaRepartoPropio.DeEnvio(envio) > Hoy().Date
                            ? "el " + RepartoPropio.FechaRepartoPropio.DeEnvio(envio).ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture) : "otro día";
                        etiqueta.Avisos.Add($"El pedido {pedido} se reparte {dia}: no se factura al imprimir la etiqueta, lo hará Facturar rutas ese día.");
                    }
                    else
                    {
                        // NestoAPI#828: la etiqueta ya está impresa; nada del papel (factura, impresión, marcas) la puede tumbar
                        try
                        {
                            entrega = entrega ?? await LeerEntregaSegura(pasos, empresa, pedido);
                            await PapelDelEnvio(pasos, envio, pedido, entrega, etiqueta, usuarioAuditoria);
                        }
                        catch (Exception ex)
                        {
                            Registrar(new Exception($"[NestoAPI#828] Etiqueta del envío {envio.Numero} hecha, pero ha fallado la facturación o el papel del pedido {pedido}: {ex.Message}", ex));
                            etiqueta.Avisos.Add($"La etiqueta está hecha, pero no se ha podido terminar la factura o el papel del pedido {pedido}: {ex.Message?.Trim()} " +
                                "Mire en Nesto si está facturado antes de repetir la etiqueta.");
                        }
                    }
                }

                return ResultadoEtiquetaPacking.Hecha(etiqueta);
            }
        }

        /// <summary>«1», «true» o «sí» en ParámetrosUsuario FacturarAlImprimirEtiqueta (o en «(defecto)»). Sin parámetro, no.</summary>
        internal bool FacturarAlImprimir(string usuario)
        {
            if (parametros == null)
            {
                return false;
            }
            try
            {
                string sinDominio = (usuario ?? string.Empty).Substring((usuario ?? string.Empty).IndexOf('\\') + 1).Trim();
                if (string.IsNullOrEmpty(sinDominio))
                {
                    return false;
                }
                string valor = parametros.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, sinDominio, PARAMETRO_FACTURAR)?.Trim();
                return valor == "1" || string.Equals(valor, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(valor, "sí", StringComparison.OrdinalIgnoreCase) || string.Equals(valor, "si", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[Etiqueta del packing #595] No se pudo leer el parámetro {PARAMETRO_FACTURAR} de {usuario}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>
        /// NestoAPI#828: el papel de la entrega con el núcleo común (<see cref="DocumentosEnPapelDelEnvio"/>): el pedido del envío y los
        /// demás de la misma entrega (servir junto, una factura por pedido; los retenidos no), la factura física impresa en el servidor
        /// y, si no ha salido, pendiente para «Reintentar impresión» o repetir F8 (sin volver a facturar).
        /// </summary>
        private async Task PapelDelEnvio(IPasosEtiquetaPacking pasos, EnviosAgencia envio, int pedido, EntregaParaEtiqueta entrega,
            EtiquetaPackingDTO etiqueta, string usuarioAuditoria)
        {
            string empresa = string.IsNullOrWhiteSpace(envio.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : envio.Empresa.Trim();
            var pedidos = new List<int> { pedido };
            foreach (int otro in (entrega?.OtrosPedidos ?? new List<int>()).Where(p => p > 0 && p != pedido).Distinct())
            {
                string motivo = await MotivoRetenidoSeguro(empresa, otro);
                if (motivo != null)
                {
                    etiqueta.Avisos.Add($"El pedido {otro} de la misma entrega no se factura al imprimir la etiqueta: {motivo.Trim()}");
                    continue;
                }
                pedidos.Add(otro);
            }

            ResultadoDocumentosEnPapel papel = await Nucleo(pasos).Procesar(new PeticionDocumentosEnPapel
            {
                Empresa = empresa,
                Pedidos = pedidos,
                Envio = envio.Numero,
                Reimpresion = etiqueta.Reimpresion,
                Usuario = usuarioAuditoria
            });
            Volcar(papel, etiqueta);
        }

        /// <summary>NestoAPI#828: el núcleo del papel con la impresora del servidor (Ariadna).</summary>
        private DocumentosEnPapelDelEnvio Nucleo(IPasosDocumentosEnPapel pasos)
        {
            var nucleo = new DocumentosEnPapelDelEnvio(pasos, ImpresoraPapel, Registrar) { Hoy = () => Hoy() };
            if (TipoDeRuta != null)
            {
                nucleo.TipoDeRuta = TipoDeRuta;
            }
            return nucleo;
        }

        /// <summary>NestoAPI#828: lo que ha pasado con el papel, en la etiqueta (avisos, facturado, documentos, «meta en la caja»).</summary>
        internal static void Volcar(ResultadoDocumentosEnPapel papel, EtiquetaPackingDTO etiqueta)
        {
            foreach (KeyValuePair<int, FacturarRutasResponseDTO> respuesta in papel.Respuestas)
            {
                etiqueta.Avisos.AddRange(ResumenFacturacion(respuesta.Value, respuesta.Key, etiqueta.Reimpresion, out bool facturado));
                if (facturado)
                {
                    etiqueta.Facturado = true;
                    etiqueta.PedidosFacturados.Add(respuesta.Key);
                }
            }
            etiqueta.Avisos.AddRange(papel.Avisos);
            etiqueta.DocumentosEnPapel.AddRange(papel.Documentos);
            etiqueta.DocumentosPendientes = papel.HayPendientes;
            foreach (DocumentoEnPapelDTO pendiente in papel.Documentos.Where(d => d.Estado == EstadosDocumentoEnPapel.PENDIENTE))
            {
                etiqueta.Avisos.Add($"{DocumentosEnPapelDelEnvio.Mayuscula(pendiente.Descripcion)} del pedido {pendiente.Pedido} no ha salido por la impresora: " +
                    "pulse «Reintentar impresión» o vuelva a pedir la etiqueta (F8). No se vuelve a facturar.");
            }
            etiqueta.MeterEnLaCaja = TextoMeterEnLaCaja(papel.Documentos
                .Where(d => d.Estado == EstadosDocumentoEnPapel.IMPRESO && !d.YaEstabaImpreso).ToList());
        }

        /// <summary>«Meta en la caja la factura NV2617986 (en «HP LaserJet Algete»).», o null si no ha salido nada.</summary>
        internal static string TextoMeterEnLaCaja(List<DocumentoEnPapelDTO> impresos)
        {
            if (impresos == null || !impresos.Any())
            {
                return null;
            }
            string documentos = string.Join(" y ", impresos.Select(d => d.Descripcion));
            List<string> impresoras = impresos.Select(d => d.Impresora?.Trim()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
            return $"Meta en la caja {documentos}" + (impresoras.Any() ? $" (en «{string.Join("», «", impresoras)}»)." : ".");
        }

        /// <summary>
        /// NestoAPI#828 (S3/S6): «Reintentar impresión» desde el packing: vuelve a imprimir el papel pendiente del pedido y de los demás
        /// de su entrega, sin facturar ni tocar la etiqueta.
        /// </summary>
        public async Task<ResultadoDocumentosEnPapel> ReintentarDocumentosEnPapel(int pedido, string empresa, IPrincipal usuario)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string usuarioAuditoria = UsuarioAuditoriaHelper.Resolver(usuario, "NestoAPI");
            using (IPasosEtiquetaPacking pasos = crearPasos(usuario))
            {
                EntregaParaEtiqueta entrega = await LeerEntregaSegura(pasos, empresa, pedido);
                EnviosAgencia envio = null;
                try
                {
                    envio = await pasos.LeerEnvioEnCurso(empresa, pedido);
                }
                catch (Exception ex)
                {
                    Registrar(ex);
                }
                var pedidos = new List<int> { pedido };
                pedidos.AddRange(entrega?.OtrosPedidos ?? new List<int>());
                return await Nucleo(pasos).ReimprimirPendientes(new PeticionDocumentosEnPapel
                {
                    Empresa = empresa,
                    Pedidos = pedidos,
                    Envio = envio?.Numero,
                    Reimpresion = true,
                    Usuario = usuarioAuditoria
                });
            }
        }

        private async Task<EntregaParaEtiqueta> LeerEntregaSegura(IPasosEtiquetaPacking pasos, string empresa, int pedido)
        {
            try
            {
                return await pasos.LeerEntrega(empresa, pedido);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[NestoAPI#828] No se ha podido leer la entrega del pedido {pedido} en el packing: solo se factura ese pedido. {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>Si no se puede saber si el otro pedido de la entrega está retenido, no se factura (lo prudente).</summary>
        private async Task<string> MotivoRetenidoSeguro(string empresa, int pedido)
        {
            try
            {
                return await MotivoPedidoRetenido(empresa, pedido, false);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[NestoAPI#828] No se ha podido saber si el pedido {pedido} está retenido: no se factura al imprimir. {ex.Message}", ex));
                return "no se ha podido comprobar si está retenido. Factúrelo desde Nesto.";
            }
        }

        /// <summary>
        /// Lo que dice la facturación, como FacturadorPedido de Nesto: el documento creado, los avisos de la factura (NIF...), los
        /// errores y si hay algo que imprimir (Ariadna no imprime documentos). En una reimpresión, «sin líneas para facturar» no
        /// se dice: ya se facturó la primera vez.
        /// </summary>
        internal static List<string> ResumenFacturacion(FacturarRutasResponseDTO respuesta, int pedido, bool reimpresion, out bool facturado)
        {
            var avisos = new List<string>();
            facturado = false;
            if (respuesta == null)
            {
                avisos.Add($"La facturación del pedido {pedido} no ha contestado. Mírelo en Nesto.");
                return avisos;
            }
            List<FacturaCreadaDTO> facturas = respuesta.Facturas ?? new List<FacturaCreadaDTO>();
            List<AlbaranCreadoDTO> albaranes = respuesta.Albaranes ?? new List<AlbaranCreadoDTO>();
            List<NotaEntregaCreadaDTO> notas = respuesta.NotasEntrega ?? new List<NotaEntregaCreadaDTO>();
            facturado = facturas.Any() || albaranes.Any() || notas.Any();

            string textoAlbaranes = string.Join(", ", albaranes.Select(a => a.NumeroAlbaran));
            if (facturas.Any())
            {
                avisos.Add($"Pedido {pedido} facturado en la factura {string.Join(", ", facturas.Select(f => f.NumeroFactura?.Trim()))}" +
                    (albaranes.Any() ? $" (albarán {textoAlbaranes})." : "."));
            }
            else if (albaranes.Any())
            {
                avisos.Add($"Albarán {textoAlbaranes} del pedido {pedido} creado.");
            }
            if (notas.Any())
            {
                avisos.Add($"Nota de entrega del pedido {pedido} procesada.");
            }
            foreach (FacturaCreadaDTO factura in facturas)
            {
                avisos.AddRange((factura.Avisos ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()));
            }
            foreach (PedidoConErrorDTO error in respuesta.PedidosConErrores ?? new List<PedidoConErrorDTO>())
            {
                if (reimpresion && string.Equals(error.TipoError?.Trim(), TIPO_ERROR_SIN_LINEAS, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                avisos.Add(error.Severidad == NivelSeveridad.Error
                    ? $"No se ha facturado: {error.TipoError?.Trim()}: {error.MensajeError?.Trim()}"
                    : error.MensajeError?.Trim());
            }
            // NestoAPI#806: con Facturacion:ImprimirDocumentosEnServidor el papel ya sale por la impresora de documentos del usuario
            // (ImpresoEnServidor). Si no se ha podido, el motivo ya viene en PedidosConErrores (y dice «Imprímalo desde Nesto»).
            List<DocumentoParaImprimir> enPapel = facturas.Select(f => f.DatosImpresion)
                .Concat(albaranes.Select(a => a.DatosImpresion))
                .Concat(notas.Select(n => n.DatosImpresion))
                .Where(d => d != null)
                .ToList();
            foreach (string impresora in enPapel.Select(d => d.ImpresoEnServidor).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct())
            {
                avisos.Add($"El documento en papel del pedido {pedido} se ha impreso en «{impresora}».");
            }
            bool avisadoFalloImpresion = (respuesta.PedidosConErrores ?? new List<PedidoConErrorDTO>())
                .Any(e => e.TipoError == ImpresionDocumentosServidor.TIPO_ERROR);
            if (enPapel.Any(d => string.IsNullOrWhiteSpace(d.ImpresoEnServidor)) && !avisadoFalloImpresion)
            {
                avisos.Add($"El pedido {pedido} lleva el documento en papel: imprímalo desde Nesto.");
            }
            return avisos.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        }

        private async Task<string> NombreSeguro(IPasosEtiquetaPacking pasos, int agencia)
        {
            try
            {
                return (await pasos.NombreAgencia(agencia))?.Trim();
            }
            catch (Exception ex)
            {
                Registrar(ex);
                return null;
            }
        }

        private static string Motivo(PasoEtiquetaAgencia paso)
        {
            string motivo = string.IsNullOrWhiteSpace(paso?.Mensaje) ? $"el servidor ha contestado {(int)(paso?.Estado ?? HttpStatusCode.InternalServerError)}" : paso.Mensaje.Trim();
            return motivo.EndsWith(".") ? motivo : motivo + ".";
        }

        internal static string TextoBultos(int bultos) => bultos == 1 ? "1 bulto" : $"{bultos} bultos";

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // El registro nunca rompe la etiqueta.
            }
        }
    }
}
