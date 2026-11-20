using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Models.RecursosHumanos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    public enum EstadoPasadaCorreosSeguimiento
    {
        /// <summary>Faltan las claves en secretos.config: no se hace nada ni se avisa.</summary>
        SinConfiguracion,
        /// <summary>Falta Scripts/Issue857_ComprasCorreosSeguimiento.sql: no se hace nada (se apunta una vez).</summary>
        SinTabla,
        Hecha,
        /// <summary>Graph ha fallado (401, 429, red…): se apunta y se sigue en la siguiente pasada.</summary>
        ConError
    }

    public class ResultadoPasadaCorreosSeguimiento
    {
        public EstadoPasadaCorreosSeguimiento Estado { get; set; }
        public int Leidos { get; set; }
        public int Guardados { get; set; }
        public int YaGuardados { get; set; }
        public int Medidos { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// NestoAPI#857 (fase 1, EN SOMBRA): una pasada del job «compras-correos-seguimiento». Lee lo nuevo del buzón de Compras, guarda
    /// SOLO los correos con algún seguimiento (o de un remitente marcado) con su agencia, código, enlace, extracto y la propuesta de
    /// proveedor y pedidos de compra candidatos; y, en un segundo paso, apunta qué candidato se recibió de verdad (para medir la
    /// asignación antes de usarla). Fase 3: con <c>aplicacion</c> (solo el job) pone la fecha de lo inequívoco y avisa de lo que queda por
    /// asignar (<see cref="ServicioAplicacionCorreosCompras"/>).
    /// </summary>
    public class ServicioCorreosSeguimientoCompras
    {
        public const int DIAS_PRIMERA_LECTURA = 7;
        public const int DIAS_MEDICION = 60;
        public const int LONGITUD_ASUNTO = 255;
        private static bool sinTablaApuntado;

        private readonly ILectorBuzonCompras lector;
        private readonly IRepositorioCorreosSeguimiento repositorio;
        private readonly IReadOnlyList<IAgenciaEntrada> agencias;
        private readonly Action<Exception> registrar;
        private readonly string empresa;
        private readonly Func<TimeSpan> horaCorte;
        private readonly Func<DateTime, bool> esFestivo;
        private readonly ServicioAplicacionCorreosCompras aplicacion;
        private readonly IReenviadorCorreosSeguimiento reenviador;
        private readonly ILectorTextoPdf lectorPdf;

        /// <param name="lector">Null = sin claves en secretos.config (no se hace nada).</param>
        /// <param name="horaCorte">Hora de corte del picking (por defecto, <see cref="HoraCortePicking.Leer"/> de la empresa).</param>
        /// <param name="esFestivo">Festivo de Algete (por defecto, <see cref="GestorFestivos.EsFestivo"/> con ALG).</param>
        /// <param name="aplicacion">Fase 3: enlazar por mismo envío, poner la fecha de lo inequívoco y avisar de lo que queda por asignar (solo el job y el reanálisis; null = nada).</param>
        /// <param name="reenviador">NestoAPI#865 (corte 3): reenviar a Compras cada correo que el JOB acaba de guardar (null = no).</param>
        /// <param name="lectorPdf">NestoAPI#857e: el texto de los PDF adjuntos de los remitentes con «leer el PDF» (por defecto, PdfPig).</param>
        public ServicioCorreosSeguimientoCompras(ILectorBuzonCompras lector, IRepositorioCorreosSeguimiento repositorio,
            Action<Exception> registrar = null, IReadOnlyList<IAgenciaEntrada> agencias = null, string empresa = null,
            Func<TimeSpan> horaCorte = null, Func<DateTime, bool> esFestivo = null, ServicioAplicacionCorreosCompras aplicacion = null,
            IReenviadorCorreosSeguimiento reenviador = null, ILectorTextoPdf lectorPdf = null)
        {
            this.aplicacion = aplicacion;
            this.reenviador = reenviador;
            this.lectorPdf = lectorPdf ?? new LectorTextoPdfPig();
            this.lector = lector;
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.registrar = registrar ?? (_ => { });
            this.agencias = agencias ?? AgenciasEntradaRegistradas.Todas;
            this.empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa;
            string empresaCorte = this.empresa;
            this.horaCorte = horaCorte ?? (() => HoraCortePicking.Leer(empresaCorte));
            this.esFestivo = esFestivo ?? (d => GestorFestivos.EsFestivo(d, Constantes.Almacenes.ALGETE));
        }

        /// <summary>Para los tests: que el «sin tabla» se vuelva a apuntar.</summary>
        internal static void OlvidarSinTablaApuntado() => sinTablaApuntado = false;

        public async Task<ResultadoPasadaCorreosSeguimiento> Pasada(DateTime ahora)
        {
            var resultado = new ResultadoPasadaCorreosSeguimiento { Estado = EstadoPasadaCorreosSeguimiento.Hecha };
            if (lector == null)
            {
                resultado.Estado = EstadoPasadaCorreosSeguimiento.SinConfiguracion;
                return resultado;
            }
            if (!repositorio.TablasExisten())
            {
                if (!sinTablaApuntado)
                {
                    sinTablaApuntado = true;
                    registrar(new Exception("[Correos de seguimiento de compras #857] (informativo) Faltan las tablas: lanzar " +
                        "Scripts/Issue857_ComprasCorreosSeguimiento.sql. Mientras, no se lee el buzón."));
                }
                resultado.Estado = EstadoPasadaCorreosSeguimiento.SinTabla;
                return resultado;
            }

            string guardado = await repositorio.LeerEstado(lector.Buzon).ConfigureAwait(false);
            LecturaBuzonCompras lectura = await lector.LeerNuevos(guardado, ahora.AddDays(-DIAS_PRIMERA_LECTURA)).ConfigureAwait(false)
                ?? new LecturaBuzonCompras { Error = "El lector no ha devuelto nada." };
            resultado.Leidos = lectura.Correos.Count;

            // La medición ANTES de decidir los correos nuevos: si un candidato de su envío ya tiene albarán, el envío ha llegado y no se
            // aplica (MismoEnvio.Llegado). Un albarán del viernes por la tarde y el correo del lunes a las 8:00 se decidirían si no a ciegas.
            int medidosAntes = lectura.Correos.Any() ? await Medir(ahora).ConfigureAwait(false) : 0;

            bool todoGuardado = true;
            if (lectura.Correos.Any())
            {
                todoGuardado = await Guardar(lectura.Correos, resultado).ConfigureAwait(false);
            }

            // Solo se avanza el delta si lo leído se ha podido guardar entero (si no, la próxima pasada lo vuelve a leer: es idempotente)
            if (todoGuardado)
            {
                if (lectura.Reiniciar)
                {
                    await repositorio.GuardarEstado(lector.Buzon, null, lectura.Error).ConfigureAwait(false);
                }
                else if (!string.IsNullOrEmpty(lectura.SiguienteEnlace) && lectura.SiguienteEnlace != guardado)
                {
                    await repositorio.GuardarEstado(lector.Buzon, lectura.SiguienteEnlace, lectura.Error).ConfigureAwait(false);
                }
            }
            if (lectura.Error != null)
            {
                resultado.Estado = EstadoPasadaCorreosSeguimiento.ConError;
                resultado.Error = lectura.Error;
                registrar(new Exception($"[Correos de seguimiento de compras #857] {lectura.Error} (se sigue en la siguiente pasada; leídos {resultado.Leidos})"));
            }

            if (aplicacion != null)
            {
                _ = await aplicacion.AvisarPendientes().ConfigureAwait(false); // fase 3: una campana por envío por asignar; nunca lanza
            }
            resultado.Medidos = medidosAntes + await Medir(ahora).ConfigureAwait(false);
            return resultado;
        }

        private Task<bool> Guardar(List<CorreoCompras> correos, ResultadoPasadaCorreosSeguimiento resultado)
        {
            List<PedidoCompraEnAire> pedidos = null;
            return GuardarCorreos(correos, resultado, async _ => pedidos ?? (pedidos = await LeerPedidos().ConfigureAwait(false)), aplicar: true, leerPdf: true,
                casarLineas: true);
        }

        /// <summary>
        /// El núcleo común del job y del histórico: analiza cada correo, guarda los que traen seguimiento (con su asignación en sombra
        /// y sus adjuntos) y cuenta. <paramref name="pedidosDe"/> da los pedidos en el aire contra los que se asigna ese correo (el
        /// job, los de ahora; el histórico, los del día del correo). False si alguno no se ha podido guardar (se reintenta: es idempotente).
        /// Con <paramref name="aplicar"/> (SOLO el job), los correos NUEVOS de la tanda pasan juntos, al final, por la fase 3
        /// (<see cref="ServicioAplicacionCorreosCompras.AlGuardar"/>): el histórico nunca escribe en compras.
        /// NestoAPI#857e: con <paramref name="leerPdf"/> (el job; el histórico no, por coste) se leen los PDF de los remitentes marcados con «leer el PDF».
        /// </summary>
        internal async Task<bool> GuardarCorreos(List<CorreoCompras> correos, ResultadoPasadaCorreosSeguimiento resultado,
            Func<CorreoCompras, Task<List<PedidoCompraEnAire>>> pedidosDe, bool aplicar = false, bool leerPdf = false, bool casarLineas = false)
        {
            // 20/11/26: las líneas en el aire y las referencias de los proveedores, solo si algún aviso trae líneas (el histórico no: casaría con lo de hoy)
            Func<List<string>, Task<DatosLineasAviso>> lineasDe = casarLineas ? CacheLineasAviso() : null;
            List<RemitenteMarcado> marcados;
            try
            {
                marcados = await repositorio.LeerRemitentes().ConfigureAwait(false) ?? new List<RemitenteMarcado>();
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido leer los remitentes marcados: " + ex.Message, ex));
                return false;
            }

            List<RemitentePaqueteRecordado> recordados = await LeerRecordados().ConfigureAwait(false);
            List<ConfirmacionPedidoProveedorFila> confirmaciones = await LeerConfirmaciones(
                (correos.Any() ? correos.Where(c => c != null).Select(c => c.Recibido).DefaultIfEmpty(DateTime.Now).Min() : DateTime.Now)
                .AddDays(-ReglaPedidoProveedor.DIAS_CONFIRMACIONES)).ConfigureAwait(false);
            var nuevas = new List<CorreoSeguimientoFila>();
            var originales = new List<(CorreoCompras Correo, CorreoSeguimientoFila Fila)>();
            bool? conAdjuntos = null;
            int adjuntosSinLeer = 0;
            bool todo = true;
            foreach (CorreoCompras correo in correos)
            {
                try
                {
                    CorreoSeguimientoFila fila = await FilaAnalizada(correo, marcados, recordados, pedidosDe, leerPdf, confirmaciones, lineasDe).ConfigureAwait(false);
                    if (fila == null)
                    {
                        continue;
                    }
                    // Los adjuntos (solo nombre, tipo y tamaño), una petición por correo GUARDADO con adjuntos y solo si hay columnas
                    if (correo.TieneAdjuntos && (conAdjuntos ?? (conAdjuntos = repositorio.AdjuntosDisponibles()).Value))
                    {
                        List<AdjuntoCorreo> adjuntos = await lector.LeerAdjuntos(correo.Id).ConfigureAwait(false);
                        if (adjuntos == null)
                        {
                            adjuntosSinLeer++;
                        }
                        else
                        {
                            ResumenAdjuntos resumen = ResumenAdjuntos.Calcular(adjuntos);
                            fila.NumAdjuntos = resumen.Numero;
                            fila.Adjuntos = resumen.Json;
                        }
                    }
                    if (await repositorio.Insertar(fila).ConfigureAwait(false))
                    {
                        resultado.Guardados++;
                        nuevas.Add(fila);
                        originales.Add((correo, fila));
                    }
                    else
                    {
                        resultado.YaGuardados++;
                    }
                }
                catch (Exception ex)
                {
                    todo = false;
                    registrar(new Exception($"[Correos de seguimiento de compras #857] No se ha podido guardar el correo del {correo?.Recibido:dd/MM/yyyy HH:mm} " +
                        $"de {correo?.RemitenteDominio}: {ex.Message}", ex));
                }
            }
            Dictionary<string, ResultadoCorreoGuardado> desenlaces = null;
            if (aplicar && aplicacion != null && nuevas.Any())
            {
                desenlaces = await aplicacion.AlGuardar(nuevas).ConfigureAwait(false); // nunca lanza
            }
            if (aplicar && reenviador != null && originales.Any())
            {
                Reenviar(originales, desenlaces);
            }
            if (adjuntosSinLeer > 0)
            {
                // Se guardan igual, sin adjuntos (NumAdjuntos NULL): una línea por tanda, no una por correo
                registrar(new Exception($"[Correos de seguimiento de compras #857] (informativo) No se han podido leer los adjuntos de {adjuntosSinLeer} " +
                    "correo(s): se guardan sin ellos."));
            }
            return todo;
        }

        /// <summary>
        /// NestoAPI#865 (corte 3): solo el job (con <c>aplicar</c>), solo lo recién insertado y con el cuerpo que Graph ha dado en esta pasada
        /// (no se guarda). Nunca lanza: el reenvío no puede romper la pasada.
        /// </summary>
        private void Reenviar(List<(CorreoCompras Correo, CorreoSeguimientoFila Fila)> originales, Dictionary<string, ResultadoCorreoGuardado> desenlaces)
        {
            try
            {
                _ = reenviador.Reenviar(originales.Select(o => (o.Correo, o.Fila, Desenlace(o.Fila, desenlaces))).ToList());
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #865] No se han podido reenviar los correos a Compras: " + ex.Message, ex));
            }
        }

        internal static ResultadoCorreoGuardado Desenlace(CorreoSeguimientoFila fila, Dictionary<string, ResultadoCorreoGuardado> desenlaces)
        {
            if (fila?.MensajeId != null && desenlaces != null && desenlaces.TryGetValue(fila.MensajeId, out ResultadoCorreoGuardado r))
            {
                return r;
            }
            return new ResultadoCorreoGuardado
            {
                Desenlace = fila?.EsSalidaNuestra == true ? DesenlaceCorreoSeguimiento.SalidaNuestra : DesenlaceCorreoSeguimiento.SinAplicar
            };
        }

        /// <summary>
        /// El análisis de un correo hasta la fila que se guarda (o se reanaliza): agencia, seguimientos, entrega, remitente del paquete y la
        /// asignación en sombra. Null si no es de los que se guardan. Un envío nuestro de salida en copia va sin asignación ni disponible.
        /// </summary>
        /// <param name="confirmaciones">NestoAPI#857g: las confirmaciones de pedido del proveedor (las nuevas de la tanda se añaden).</param>
        private async Task<CorreoSeguimientoFila> FilaAnalizada(CorreoCompras correo, List<RemitenteMarcado> marcados,
            List<RemitentePaqueteRecordado> recordados, Func<CorreoCompras, Task<List<PedidoCompraEnAire>>> pedidosDe, bool leerPdf,
            List<ConfirmacionPedidoProveedorFila> confirmaciones = null, Func<List<string>, Task<DatosLineasAviso>> lineasDe = null)
        {
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(correo, agencias, marcados);
            if (analisis.Confirmacion != null)
            {
                await GuardarConfirmacion(correo, analisis, pedidosDe, confirmaciones).ConfigureAwait(false);
                return null; // no es un envío: ni se guarda como correo de seguimiento, ni pone fecha, ni se reenvía
            }
            if (!analisis.Guardar || correo.Clave() == null)
            {
                return null;
            }
            AsignacionPedidoCompra asignacion = null;
            List<PedidoCompraEnAire> pedidos = null;
            if (!analisis.EsSalidaNuestra)
            {
                pedidos = await pedidosDe(correo).ConfigureAwait(false);
                asignacion = AsignadorPedidosCompra.Asignar(correo, analisis.RemitentesOriginales, pedidos, analisis.RemitentePaquete, recordados);
            }
            // NestoAPI#857e: el PDF del aviso de salida (solo si el remitente lo pide; ni el histórico ni las salidas nuestras)
            LecturaPdfsCorreo pdfs = leerPdf && analisis.RemitenteMarcado?.LeerPdf == true && !analisis.EsSalidaNuestra
                ? await LeerTextoPdfs(correo).ConfigureAwait(false)
                : null;
            AvisoSalidaPdf aviso = null;
            if (pdfs?.Texto != null)
            {
                aviso = ExtractorAvisoSalida.Extraer(pdfs.Texto, correo.Recibido, AvisoSalidaProveedor.DelProveedor(pedidos, asignacion), pedidos, agencias);
                asignacion = AvisoSalidaProveedor.Completar(analisis, asignacion, aviso);
            }
            // NestoAPI#857g: la referencia del remitente («Ref.: SO252231…», MBE) es el pedido de una confirmación del proveedor: su pedido nuestro
            if (!analisis.EsSalidaNuestra && analisis.ReferenciasRemitente.Any())
            {
                asignacion = ReglaPedidoProveedor.Completar(asignacion, analisis.ReferenciasRemitente, confirmaciones, pedidos);
            }
            // 20/11/26: el aviso de salida del proveedor sin su número de pedido (Weelko) se enlaza por la fecha de la compra
            if (!analisis.EsSalidaNuestra && analisis.FechaCompraProveedor.HasValue)
            {
                asignacion = ReglaPedidoProveedor.CompletarPorFechaCompra(asignacion, analisis.FechaCompraProveedor, correo.RemitenteDireccion,
                    confirmaciones, pedidos);
            }
            // 20/11/26: los productos del aviso casan con las líneas en el aire de los pedidos de su proveedor: el pedido (si es uno) y sus líneas
            ResultadoLineasAviso lineas = null;
            if (lineasDe != null && !analisis.EsSalidaNuestra && analisis.LineasAviso.Any())
            {
                List<string> proveedores = ReglaLineasAvisoSalida.Proveedores(asignacion);
                if (proveedores.Any())
                {
                    DatosLineasAviso datos = await lineasDe(proveedores).ConfigureAwait(false) ?? new DatosLineasAviso();
                    var suyos = new HashSet<string>(proveedores, StringComparer.OrdinalIgnoreCase);
                    lineas = ReglaLineasAvisoSalida.Casar(analisis.LineasAviso, datos.Referencias.Where(r => suyos.Contains(r.Proveedor?.Trim() ?? string.Empty)),
                        datos.Lineas.Where(l => suyos.Contains(l.Proveedor?.Trim() ?? string.Empty)));
                    asignacion = ReglaLineasAvisoSalida.Completar(asignacion, lineas);
                }
            }
            CorreoSeguimientoFila fila = Fila(correo, analisis, asignacion);
            fila.LineasAviso = LineasAvisoCorreo.Serializar(analisis.LineasAviso, lineas);
            fila.DisponibleEstimado = DisponibleEstimado(analisis);
            fila.AvisoProveedor = analisis.AvisoProveedor;
            fila.PedidoEnAdjunto = aviso?.Pedido?.Pedido;
            fila.AdjuntoLeido = pdfs?.Estado.ToString();
            return fila;
        }

        /// <summary>
        /// NestoAPI#857e: los PDF del correo (como mucho <see cref="LimitesPdfAdjuntos.MAXIMO_PDFS"/>, de <see cref="LimitesPdfAdjuntos.MAXIMO_BYTES"/>
        /// y <see cref="LimitesPdfAdjuntos.MAXIMO_PAGINAS"/> páginas), leídos en memoria: el contenido se suelta al salir y nunca se guarda. Un
        /// PDF escaneado, dañado o que Graph no da no rompe nada: el correo sigue como si no lo tuviera y el estado lo dice. Nunca lanza.
        /// </summary>
        private async Task<LecturaPdfsCorreo> LeerTextoPdfs(CorreoCompras correo)
        {
            if (!correo.TieneAdjuntos)
            {
                return new LecturaPdfsCorreo { Estado = EstadoPdfAdjunto.SinPdf };
            }
            try
            {
                List<PdfAdjunto> descargados = await lector.LeerPdfs(correo.Id, LimitesPdfAdjuntos.MAXIMO_PDFS, LimitesPdfAdjuntos.MAXIMO_BYTES).ConfigureAwait(false);
                if (descargados == null)
                {
                    return new LecturaPdfsCorreo { Estado = EstadoPdfAdjunto.NoDescargado };
                }
                var estados = new List<EstadoPdfAdjunto>();
                var textos = new List<string>();
                foreach (PdfAdjunto pdf in descargados.Where(p => p != null).Take(LimitesPdfAdjuntos.MAXIMO_PDFS))
                {
                    if (pdf.Contenido == null)
                    {
                        estados.Add(pdf.Tamano > LimitesPdfAdjuntos.MAXIMO_BYTES ? EstadoPdfAdjunto.Grande : EstadoPdfAdjunto.NoDescargado);
                        continue;
                    }
                    ResultadoTextoPdf leido = lectorPdf.Leer(pdf.Contenido, LimitesPdfAdjuntos.MAXIMO_PAGINAS)
                        ?? new ResultadoTextoPdf { Estado = EstadoPdfAdjunto.Ilegible };
                    estados.Add(leido.Estado);
                    if (leido.Estado == EstadoPdfAdjunto.Leido && !string.IsNullOrWhiteSpace(leido.Texto))
                    {
                        textos.Add(leido.Texto);
                    }
                }
                return new LecturaPdfsCorreo
                {
                    Estado = AvisoSalidaProveedor.EstadoConjunto(estados),
                    Texto = textos.Any() ? string.Join("\n", textos) : null
                };
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Correos de seguimiento de compras #857e] No se ha podido leer el PDF del correo del {correo.Recibido:dd/MM/yyyy HH:mm} " +
                    $"de {correo.RemitenteDominio}: {ex.Message} (se trata como si no lo tuviera)", ex));
                return new LecturaPdfsCorreo { Estado = EstadoPdfAdjunto.Ilegible };
            }
        }

        /// <summary>
        /// NestoAPI#857g: guarda la confirmación de un pedido del proveedor con nuestros pedidos candidatos (<see cref="ReglaPedidoConfirmado"/>).
        /// Sin Scripts/Issue857g, nada. Nunca lanza: si falla, se apunta y la tanda sigue.
        /// </summary>
        private async Task GuardarConfirmacion(CorreoCompras correo, AnalisisCorreoSeguimiento analisis,
            Func<CorreoCompras, Task<List<PedidoCompraEnAire>>> pedidosDe, List<ConfirmacionPedidoProveedorFila> confirmaciones)
        {
            try
            {
                if (correo.Clave() == null || !repositorio.ConfirmacionesDisponibles())
                {
                    return;
                }
                List<PedidoCompraEnAire> pedidos = await pedidosDe(correo).ConfigureAwait(false) ?? new List<PedidoCompraEnAire>();
                AsignacionPedidoCompra asignacion = AsignadorPedidosCompra.Asignar(correo, analisis.RemitentesOriginales, pedidos);
                List<CandidatoPedidoCompra> candidatos = ReglaPedidoConfirmado.Elegir(asignacion.Candidatos, pedidos,
                    analisis.Confirmacion.ReferenciaCliente, correo.Recibido);
                List<string> proveedores = candidatos.Select(c => c.Proveedor?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var fila = new ConfirmacionPedidoProveedorFila
                {
                    MensajeId = correo.Clave(),
                    Recibido = correo.Recibido,
                    RemitenteDireccion = TextoCorreo.Recortar(correo.RemitenteDireccion?.ToLowerInvariant(), 255),
                    Formato = TextoCorreo.Recortar(analisis.Confirmacion.Formato, 50),
                    PedidoProveedor = TextoCorreo.Recortar(analisis.Confirmacion.PedidoProveedor, 40),
                    ReferenciaCliente = TextoCorreo.Recortar(analisis.Confirmacion.ReferenciaCliente, 40),
                    Agencia = TextoCorreo.Recortar(analisis.Confirmacion.Agencia?.Nombre, 50),
                    ProveedorPropuesto = proveedores.Count == 1 ? proveedores[0] : asignacion.ProveedorPropuesto,
                    PedidosCandidatos = AsignadorPedidosCompra.Serializar(candidatos)
                };
                if (await repositorio.GuardarConfirmacion(fila).ConfigureAwait(false) && confirmaciones != null)
                {
                    _ = confirmaciones.RemoveAll(c => c.MensajeId == fila.MensajeId);
                    confirmaciones.Add(fila);
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Correos de seguimiento de compras #857g] No se ha podido guardar la confirmación del pedido {analisis?.Confirmacion?.PedidoProveedor} " +
                    $"del {correo?.Recibido:dd/MM/yyyy HH:mm}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// 20/11/26: las líneas en el aire y las referencias de ProveedoresProducto de los proveedores que se piden, leídas una vez por proveedor
        /// y tanda (normalmente, una consulta por tanda). Si falla, nada (se apunta): el correo se asigna y aplica como antes.
        /// </summary>
        private Func<List<string>, Task<DatosLineasAviso>> CacheLineasAviso()
        {
            var leidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var datos = new DatosLineasAviso();
            return async proveedores =>
            {
                List<string> faltan = (proveedores ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p) && !leidos.Contains(p.Trim()))
                    .Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (faltan.Any())
                {
                    foreach (string p in faltan)
                    {
                        _ = leidos.Add(p);
                    }
                    try
                    {
                        DatosLineasAviso nuevos = await repositorio.LeerLineasAviso(faltan).ConfigureAwait(false) ?? new DatosLineasAviso();
                        datos.Lineas.AddRange(nuevos.Lineas ?? new List<LineaEnAireAviso>());
                        datos.Referencias.AddRange(nuevos.Referencias ?? new List<ReferenciaProductoProveedor>());
                    }
                    catch (Exception ex)
                    {
                        registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido leer las líneas en el aire para casar el aviso: " + ex.Message, ex));
                    }
                }
                return datos;
            };
        }

        /// <summary>NestoAPI#857g: las confirmaciones desde ese día. Sin Scripts/Issue857g o si falla, ninguna (se guarda igual, sin enlazar).</summary>
        private async Task<List<ConfirmacionPedidoProveedorFila>> LeerConfirmaciones(DateTime desde)
        {
            try
            {
                return repositorio.ConfirmacionesDisponibles()
                    ? await repositorio.LeerConfirmaciones(desde).ConfigureAwait(false) ?? new List<ConfirmacionPedidoProveedorFila>()
                    : new List<ConfirmacionPedidoProveedorFila>();
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857g] No se han podido leer las confirmaciones de pedido de los proveedores: " + ex.Message, ex));
                return new List<ConfirmacionPedidoProveedorFila>();
            }
        }

        /// <summary>La memoria de remitentes del paquete (fase 3). Sin Scripts/Issue857d o si falla, ninguno (se guarda igual).</summary>
        private async Task<List<RemitentePaqueteRecordado>> LeerRecordados()
        {
            try
            {
                return repositorio.AplicacionDisponible()
                    ? await repositorio.LeerRemitentesPaquete().ConfigureAwait(false) ?? new List<RemitentePaqueteRecordado>()
                    : new List<RemitentePaqueteRecordado>();
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido leer los remitentes del paquete recordados: " + ex.Message, ex));
                return new List<RemitentePaqueteRecordado>();
            }
        }

        private async Task<List<PedidoCompraEnAire>> LeerPedidos()
        {
            try
            {
                return await repositorio.LeerPedidosEnAire(empresa).ConfigureAwait(false) ?? new List<PedidoCompraEnAire>();
            }
            catch (Exception ex)
            {
                // Sin pedidos se guarda igual, sin candidatos
                registrar(new Exception("[Correos de seguimiento de compras #857] No se han podido leer los pedidos de compra en el aire: " + ex.Message, ex));
                return new List<PedidoCompraEnAire>();
            }
        }

        internal static CorreoSeguimientoFila Fila(CorreoCompras correo, AnalisisCorreoSeguimiento analisis, AsignacionPedidoCompra asignacion)
        {
            SeguimientoCorreo primero = analisis.Seguimientos.FirstOrDefault();
            return new CorreoSeguimientoFila
            {
                MensajeId = correo.Clave(),
                Recibido = correo.Recibido,
                RemitenteDireccion = TextoCorreo.Recortar(correo.RemitenteDireccion?.ToLowerInvariant(), 255),
                RemitenteDominio = correo.RemitenteDominio,
                Asunto = TextoCorreo.Recortar(correo.Asunto, LONGITUD_ASUNTO),
                Agencia = TextoCorreo.Recortar(analisis.Agencia, 50),
                Codigo = TextoCorreo.Recortar(primero?.Codigo, 50),
                Url = TextoCorreo.Recortar(primero?.Url, 1000),
                Seguimientos = analisis.Seguimientos.Count > 1
                    ? TextoCorreo.Recortar(JsonConvert.SerializeObject(analisis.Seguimientos.Take(20)), 4000)
                    : null,
                Extracto = TextoCorreo.Recortar(analisis.Extracto, 600),
                ProveedorPropuesto = asignacion?.ProveedorPropuesto,
                PedidosCandidatos = analisis.EsSalidaNuestra ? null : AsignadorPedidosCompra.Serializar(asignacion?.Candidatos),
                EntregaEstimadaDia = analisis.EntregaEstimada?.Dia,
                EntregaEstimadaDesde = analisis.EntregaEstimada?.Desde,
                EntregaEstimadaHasta = analisis.EntregaEstimada?.Hasta,
                EsSalidaNuestra = analisis.EsSalidaNuestra,
                RemitentePaquete = TextoCorreo.Recortar(analisis.RemitentePaquete, ExtractorRemitentePaquete.LONGITUD),
                EstadoEnvio = analisis.EstadoEnvio?.ToString(),
                OrigenEntrega = analisis.EntregaEstimada != null ? analisis.OrigenEntrega : null
            };
        }

        /// <summary>
        /// Desde cuándo se podría servir lo que anuncia el correo (<see cref="ReglaDisponibleCompra"/>, con la hora de corte del
        /// picking y los festivos de Algete). Null si no anuncia entrega, si es una salida nuestra o si falla (se apunta).
        /// </summary>
        internal DateTime? DisponibleEstimado(AnalisisCorreoSeguimiento analisis)
        {
            if (analisis?.EntregaEstimada == null || analisis.EsSalidaNuestra)
            {
                return null;
            }
            try
            {
                return ReglaDisponibleCompra.Calcular(analisis.EntregaEstimada, horaCorte(), esFestivo);
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se ha podido calcular el disponible estimado: " + ex.Message, ex));
                return null;
            }
        }

        public const int DIAS_MAXIMOS_REANALISIS = 30;
        public const int MAXIMO_PAGINAS_REANALISIS = 200;

        /// <summary>
        /// Fase 3: vuelve a analizar lo YA guardado de los últimos <paramref name="dias"/> días, releyendo el buzón por fechas (el mismo
        /// <see cref="ILectorBuzonCompras.LeerPagina"/> del histórico, sin tocar el delta). Solo actualiza lo derivado del análisis de los
        /// correos sin aplicar ni descartar (con los pedidos en el aire de AHORA) y rehace los enlaces por mismo envío; no aplica fechas ni
        /// avisa. NestoAPI#857f: INSERTA los que ahora sí se guardan y antes no (los avisos de SEUR sin código de estos días), sin aplicar,
        /// reenviar ni avisar. NestoAPI#857e: SÍ lee los PDF de los remitentes con «leer el PDF» (son pocos días y pocos correos).
        /// </summary>
        public async Task<ResultadoReanalisisCorreos> Reanalizar(int dias, DateTime ahora)
        {
            var resultado = new ResultadoReanalisisCorreos { Estado = EstadoPasadaCorreosSeguimiento.Hecha, Dias = dias };
            if (lector == null)
            {
                resultado.Estado = EstadoPasadaCorreosSeguimiento.SinConfiguracion;
                return resultado;
            }
            if (!repositorio.TablasExisten() || !repositorio.AplicacionDisponible())
            {
                resultado.Estado = EstadoPasadaCorreosSeguimiento.SinTabla;
                return resultado;
            }
            List<RemitenteMarcado> marcados = await repositorio.LeerRemitentes().ConfigureAwait(false) ?? new List<RemitenteMarcado>();
            List<RemitentePaqueteRecordado> recordados = await LeerRecordados().ConfigureAwait(false);
            List<ConfirmacionPedidoProveedorFila> confirmaciones = await LeerConfirmaciones(ahora.AddDays(-dias - ReglaPedidoProveedor.DIAS_CONFIRMACIONES)).ConfigureAwait(false);
            List<PedidoCompraEnAire> pedidos = null;
            Func<List<string>, Task<DatosLineasAviso>> lineasDe = CacheLineasAviso();
            string enlace = null;
            for (int pagina = 0; pagina < MAXIMO_PAGINAS_REANALISIS; pagina++)
            {
                LecturaBuzonCompras lectura = await lector.LeerPagina(enlace, ahora.AddDays(-dias), ahora).ConfigureAwait(false)
                    ?? new LecturaBuzonCompras { Error = "El lector no ha devuelto nada." };
                if (lectura.Error != null)
                {
                    resultado.Estado = EstadoPasadaCorreosSeguimiento.ConError;
                    resultado.Error = lectura.Error;
                    registrar(new Exception($"[Correos de seguimiento de compras #857, reanálisis] {lectura.Error} (leídos {resultado.Leidos}, actualizados {resultado.Actualizados})"));
                    break;
                }
                foreach (CorreoCompras correo in lectura.Correos)
                {
                    resultado.Leidos++;
                    try
                    {
                        CorreoSeguimientoFila fila = await FilaAnalizada(correo, marcados, recordados,
                            async _ => pedidos ?? (pedidos = await LeerPedidos().ConfigureAwait(false)), leerPdf: true, confirmaciones, lineasDe).ConfigureAwait(false);
                        if (fila != null && await repositorio.ActualizarAnalisis(fila).ConfigureAwait(false))
                        {
                            resultado.Actualizados++;
                        }
                        else if (fila != null && await repositorio.IdDe(fila.MensajeId).ConfigureAwait(false) == null
                            && await repositorio.Insertar(fila).ConfigureAwait(false))
                        {
                            resultado.Insertados++;
                        }
                    }
                    catch (Exception ex)
                    {
                        resultado.Fallidos++;
                        registrar(new Exception($"[Correos de seguimiento de compras #857, reanálisis] No se ha podido reanalizar el correo del {correo?.Recibido:dd/MM/yyyy HH:mm}: {ex.Message}", ex));
                    }
                }
                enlace = lectura.SiguienteEnlace;
                if (string.IsNullOrEmpty(enlace))
                {
                    break;
                }
            }
            if (aplicacion != null)
            {
                resultado.Enlazados = await aplicacion.Reenlazar(ahora.AddDays(-dias)).ConfigureAwait(false);
            }
            return resultado;
        }

        /// <summary>Segundo paso: de los guardados con candidatos y sin real, si alguno de sus candidatos ya tiene albarán, cuál.</summary>
        internal async Task<int> Medir(DateTime ahora)
        {
            try
            {
                List<CorreoSeguimientoFila> pendientes = await repositorio.LeerPendientesDeMedir(ahora.AddDays(-DIAS_MEDICION)).ConfigureAwait(false);
                return await MedirLote(pendientes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrar(new Exception("[Correos de seguimiento de compras #857] No se ha podido medir la asignación: " + ex.Message, ex));
                return 0;
            }
        }

        /// <summary>El núcleo de la medición (job e histórico): albaranes de los candidatos por clave y, para cada fila, el real.</summary>
        internal async Task<int> MedirLote(List<CorreoSeguimientoFila> pendientes)
        {
            if (pendientes == null || !pendientes.Any())
            {
                return 0;
            }
            var candidatos = pendientes.ToDictionary(p => p.Id, p => AsignadorPedidosCompra.Deserializar(p.PedidosCandidatos));
            List<CandidatoPedidoCompra> todos = candidatos.Values.SelectMany(c => c).ToList();
            if (!todos.Any())
            {
                return 0;
            }
            List<AlbaranPedidoCompra> albaranes = await repositorio.LeerAlbaranes(todos).ConfigureAwait(false) ?? new List<AlbaranPedidoCompra>();
            int medidos = 0;
            foreach (CorreoSeguimientoFila pendiente in pendientes)
            {
                AlbaranPedidoCompra real = AsignadorPedidosCompra.DecidirReal(pendiente.Recibido, candidatos[pendiente.Id], albaranes);
                if (real != null)
                {
                    await repositorio.GuardarReal(pendiente.Id, real).ConfigureAwait(false);
                    medidos++;
                }
            }
            return medidos;
        }
    }

    /// <summary>NestoAPI#857 (fase 3): lo que ha hecho POST api/Compras/AgenciasEntrada/Correos/Reanalizar.</summary>
    public class ResultadoReanalisisCorreos
    {
        public EstadoPasadaCorreosSeguimiento Estado { get; set; }
        public int Dias { get; set; }
        public int Leidos { get; set; }
        /// <summary>Correos ya guardados, sin aplicar ni descartar, cuyo análisis se ha vuelto a escribir.</summary>
        public int Actualizados { get; set; }
        /// <summary>Correos a los que se han puesto los candidatos de otro del mismo envío.</summary>
        public int Enlazados { get; set; }
        /// <summary>NestoAPI#857f: correos que antes no se guardaban y ahora sí (se insertan sin aplicar, reenviar ni avisar).</summary>
        public int Insertados { get; set; }
        public int Fallidos { get; set; }
        public string Error { get; set; }
    }

    /// <summary>NestoAPI#857: remitente captado aún sin marcar (para que Compras diga «este es GLS»).</summary>
    public class CandidatoRemitenteDTO
    {
        public string Remitente { get; set; }
        public string Dominio { get; set; }
        public int Correos { get; set; }
        public DateTime UltimoRecibido { get; set; }
        public string UltimoAsunto { get; set; }
        public List<string> DominiosEnlaces { get; set; } = new List<string>();
        public List<string> AgenciasReconocidas { get; set; } = new List<string>();
    }

    public static class CandidatosRemitentes
    {
        /// <summary>Los remitentes de lo captado que no están marcados (activos), con más correos primero.</summary>
        public static List<CandidatoRemitenteDTO> Calcular(IEnumerable<CorreoSeguimientoFila> correos, IEnumerable<RemitenteMarcado> marcados)
        {
            List<RemitenteMarcado> activos = (marcados ?? Enumerable.Empty<RemitenteMarcado>()).Where(m => m != null && m.Activo).ToList();
            return (correos ?? Enumerable.Empty<CorreoSeguimientoFila>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.RemitenteDireccion))
                .Where(c => !activos.Any(m => m.Casa(c.RemitenteDireccion)))
                .GroupBy(c => c.RemitenteDireccion.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    CorreoSeguimientoFila ultimo = g.OrderByDescending(c => c.Recibido).First();
                    return new CandidatoRemitenteDTO
                    {
                        Remitente = g.Key,
                        Dominio = TextoCorreo.Dominio(g.Key),
                        Correos = g.Count(),
                        UltimoRecibido = ultimo.Recibido,
                        UltimoAsunto = ultimo.Asunto,
                        DominiosEnlaces = g.SelectMany(Urls).Select(ExtractorEnlaces.Host).Where(h => h != null).Distinct().OrderBy(h => h).ToList(),
                        AgenciasReconocidas = g.Select(c => c.Agencia).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().OrderBy(a => a).ToList()
                    };
                })
                .OrderByDescending(c => c.Correos).ThenByDescending(c => c.UltimoRecibido)
                .ToList();
        }

        private static IEnumerable<string> Urls(CorreoSeguimientoFila fila)
        {
            if (!string.IsNullOrWhiteSpace(fila.Url))
            {
                yield return fila.Url;
            }
            if (!string.IsNullOrWhiteSpace(fila.Seguimientos))
            {
                List<SeguimientoCorreo> todos;
                try
                {
                    todos = JsonConvert.DeserializeObject<List<SeguimientoCorreo>>(fila.Seguimientos) ?? new List<SeguimientoCorreo>();
                }
                catch (JsonException)
                {
                    todos = new List<SeguimientoCorreo>();
                }
                foreach (SeguimientoCorreo s in todos.Where(s => !string.IsNullOrWhiteSpace(s.Url)))
                {
                    yield return s.Url;
                }
            }
        }
    }

    /// <summary>
    /// NestoAPI#857: el job de Hangfire «compras-correos-seguimiento», cada 30 minutos de lunes a viernes de 8:00 a 18:30 (hora de
    /// Madrid). Sin las claves de secretos.config no hace nada; sin las tablas, lo apunta una vez. Nunca lanza.
    /// </summary>
    public static class CorreosSeguimientoComprasJobsService
    {
        public const string JOB_ID = "compras-correos-seguimiento";
        public const string CRON = "*/30 8-18 * * 1-5";
        private const string USUARIO_ELMAH = "Sistema (correos de seguimiento de compras)";

        public static async Task Pasada()
        {
            try
            {
                ConfiguracionBuzonCompras configuracion = ConfiguracionBuzonCompras.Leer(clave => ConfigurationManager.AppSettings[clave]);
                ILectorBuzonCompras lector = configuracion == null ? null : new LectorBuzonComprasGraph(configuracion);
                var repositorio = new RepositorioCorreosSeguimiento();
                Action<Exception> registrar = ex => ElmahHelper.Log(ex, USUARIO_ELMAH);
                var aplicacion = new ServicioAplicacionCorreosCompras(repositorio, avisador: new AvisadorCorreosPorAsignar(), registrar: registrar);
                var reenviador = new ReenviadorCorreosSeguimiento(new ServicioCorreoElectronico(), registrar: registrar);
                var servicio = new ServicioCorreosSeguimientoCompras(lector, repositorio, registrar, aplicacion: aplicacion, reenviador: reenviador,
                    lectorPdf: new LectorTextoPdfPig());
                _ = await servicio.Pasada(DateTime.Now).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Correos de seguimiento de compras #857] Error en la pasada: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }
    }
}
