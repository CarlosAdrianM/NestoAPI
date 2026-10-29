using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
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
    /// <item>Registrarlo en la agencia (/Tramitar) si su flujo es «registrar al imprimir» (CTT, Innovatrans) y aún no tiene
    /// albarán.</item>
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
        /// <summary>Cajas distintas (una caja compartida por dos pedidos cuenta una vez).</summary>
        public int Bultos { get; set; }
        public int BultosSinFoto { get; set; }
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
            return new EntregaParaEtiqueta
            {
                Bultos = cajas.Count,
                BultosSinFoto = cajas.Count(c => !c.Any(b => b.TieneFoto)),
                OtrosPedidos = entrega.Pedidos.Select(p => p.Pedido).Where(p => p != pedido).Distinct().OrderBy(p => p).ToList()
            };
        }

        /// <summary>Sin packing en curso: los bultos del pedido (GET Almacen/Pedidos/{pedido}/Bultos), por caja.</summary>
        public static EntregaParaEtiqueta De(IEnumerable<BultoAlmacenDTO> bultosDelPedido)
        {
            List<IGrouping<string, BultoAlmacenDTO>> cajas = (bultosDelPedido ?? Enumerable.Empty<BultoAlmacenDTO>()).GroupBy(ClaveCaja).ToList();
            return new EntregaParaEtiqueta
            {
                Bultos = cajas.Count,
                BultosSinFoto = cajas.Count(c => !c.Any(b => b.TieneFoto))
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
    public interface IPasosEtiquetaPacking : IDisposable
    {
        /// <summary>El último envío EN CURSO (Estado 0) del pedido, como el «envío actual» de Agencias; null si no hay.</summary>
        Task<EnviosAgencia> LeerEnvioEnCurso(string empresa, int pedido);
        /// <summary>Las cajas de la entrega en el packing; null si el pedido no tiene packing ni bultos.</summary>
        Task<EntregaParaEtiqueta> LeerEntrega(string empresa, int pedido);
        Task<ResultadoEnvioDesdePedido> CrearDesdePedido(CrearEnvioDesdePedidoDTO peticion, string usuario);
        /// <summary>POST api/EnviosAgencias/{id}/Tramitar.</summary>
        Task<PasoEtiquetaAgencia> Tramitar(int envio);
        /// <summary>POST api/EnviosAgencias/{id}/ImprimirEtiqueta (impresora null = la del usuario).</summary>
        Task<PasoEtiquetaAgencia> Imprimir(int envio, string impresora);
        /// <summary>POST api/FacturacionRutas/FacturarPedido. Lanza con el motivo si no se ha podido.</summary>
        Task<FacturarRutasResponseDTO> Facturar(string empresa, int pedido);
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
        internal Func<string, int, Task<string>> MotivoPedidoRetenido { get; set; } = PedidosVenta.Retencion.GuardiaPedidoRetenido.Motivo;

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
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string usuarioAuditoria = UsuarioAuditoriaHelper.Resolver(usuario, "NestoAPI");

            string retenido = await MotivoPedidoRetenido(empresa, pedido);
            if (retenido != null)
            {
                return ResultadoEtiquetaPacking.Invalida(CodigosEtiquetaPacking.PEDIDO_RETENIDO, retenido);
            }

            using (IPasosEtiquetaPacking pasos = crearPasos(usuario))
            {
                var etiqueta = new EtiquetaPackingDTO { Pedido = pedido };

                // 1. El envío: el de un intento anterior o uno nuevo con las cajas de la entrega.
                EnviosAgencia envio = await pasos.LeerEnvioEnCurso(empresa, pedido);
                if (envio == null)
                {
                    EntregaParaEtiqueta entrega = await pasos.LeerEntrega(empresa, pedido);
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
                        Dimensiones = peticion.Dimensiones
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
                    }

                    envio = creado.Envio.Envio;
                    etiqueta.EnvioNuevo = true;
                    etiqueta.Avisos.AddRange(creado.Envio.Avisos ?? new List<string>());
                    // Con los bultos tecleados no se cierran cajas: el aviso de cajas sin foto no tiene sentido
                    if (!bultosTecleados && entrega?.BultosSinFoto > 0)
                    {
                        etiqueta.Avisos.Add(entrega.BultosSinFoto == 1
                            ? "Hay 1 caja sin foto."
                            : $"Hay {entrega.BultosSinFoto} cajas sin foto.");
                    }
                    if (entrega?.OtrosPedidos?.Count > 0)
                    {
                        etiqueta.Avisos.Add($"En la misma entrega van también {(entrega.OtrosPedidos.Count == 1 ? "el pedido" : "los pedidos")} " +
                            $"{string.Join(", ", entrega.OtrosPedidos)}: esta etiqueta es para todas las cajas, pero el envío es del pedido {pedido}. " +
                            "Si hay que añadirlos al envío, hágalo desde Agencias de Nesto.");
                    }
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
                if (FacturarAlImprimir(usuarioAuditoria))
                {
                    await Facturar(pasos, envio, pedido, etiqueta);
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

        private async Task Facturar(IPasosEtiquetaPacking pasos, EnviosAgencia envio, int pedido, EtiquetaPackingDTO etiqueta)
        {
            string empresa = string.IsNullOrWhiteSpace(envio.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : envio.Empresa.Trim();
            FacturarRutasResponseDTO respuesta;
            try
            {
                respuesta = await pasos.Facturar(empresa, pedido);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[Etiqueta del packing #595] Etiqueta del envío {envio.Numero} impresa, pero no se pudo facturar el pedido {pedido}: {ex.Message}", ex));
                etiqueta.Avisos.Add($"No se ha podido facturar el pedido {pedido}: {ex.Message.Trim()} Facture desde Nesto.");
                return;
            }
            List<string> avisos = ResumenFacturacion(respuesta, pedido, etiqueta.Reimpresion, out bool facturado);
            etiqueta.Facturado = facturado;
            etiqueta.Avisos.AddRange(avisos);
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
            bool hayQueImprimir = facturas.Any(f => f.DatosImpresion != null) || albaranes.Any(a => a.DatosImpresion != null)
                || notas.Any(n => n.DatosImpresion != null);
            if (hayQueImprimir)
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
