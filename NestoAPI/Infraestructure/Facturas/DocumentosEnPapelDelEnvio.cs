using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#828: lo que necesita el núcleo del papel del packing, detrás de una interfaz para probarlo sin BD. La implementación
    /// real (<see cref="PasosDocumentosEnPapelApi"/>) factura con FacturarPedido (el núcleo de Facturar rutas, #592) y regenera los
    /// documentos con <see cref="GestorFacturacionRutas.ObtenerDocumentosImpresion"/>. La etiqueta del packing de Ariadna
    /// (<see cref="Agencias.IPasosEtiquetaPacking"/>) la extiende.
    /// </summary>
    public interface IPasosDocumentosEnPapel
    {
        /// <summary>POST api/FacturacionRutas/FacturarPedido. Lanza con el motivo si no se ha podido.</summary>
        Task<FacturarRutasResponseDTO> Facturar(string empresa, int pedido);

        /// <summary>
        /// NestoAPI#765: ¿es una nota (de entrega o recogida) de una ruta de reparto propio? Esas no se procesan al imprimir la etiqueta:
        /// las procesa Facturar rutas, que crea la parada e imprime el documento NE (#758).
        /// </summary>
        Task<bool> EsNotaDeRepartoPropio(string empresa, int pedido);

        /// <summary>NestoAPI#828: la ruta del pedido (para <see cref="ITipoRuta.ImprimeAlCerrarEnvio"/>); null si no existe.</summary>
        Task<string> RutaDelPedido(string empresa, int pedido);

        /// <summary>NestoAPI#828: las marcas del papel del packing del pedido (vacía sin tabla).</summary>
        Task<List<DocumentoEnPapelDTO>> LeerMarcas(string empresa, int pedido);

        /// <summary>NestoAPI#828: pone la marca (nunca lanza).</summary>
        Task Marcar(DocumentoEnPapelDTO marca, string aplicacion);

        /// <summary>NestoAPI#828: el documento ya creado, otra vez en PDF con sus copias y su bandeja (sin volver a facturar).</summary>
        Task<DocumentosImpresionPedidoDTO> Regenerar(string empresa, int pedido, string tipo, string documento);
    }

    /// <summary>
    /// NestoAPI#828 (estrategia por aplicación): quién imprime el papel del packing. Ariadna: el servidor
    /// (<see cref="ImpresoraPapelPackingServidor"/>), al facturar ya lo hace <see cref="ImpresionDocumentosServidor"/>. Nesto: en local
    /// (<see cref="ImpresoraPapelPackingLocal"/>), que no imprime nada aquí: devuelve los PDF y Nesto confirma cuando han salido.
    /// </summary>
    public interface IImpresoraPapelPacking
    {
        /// <summary>Uno de <see cref="AplicacionesDocumentoEnPapel"/>.</summary>
        string Aplicacion { get; }

        /// <summary>Imprime los documentos regenerados al reintentar (rellena ImpresoEnServidor o el Aviso). Nunca lanza.</summary>
        void Imprimir(List<DocumentoAImprimirEnServidor> documentos, string usuario);
    }

    /// <summary>NestoAPI#828: Ariadna. Imprime en la ImpresoraDocumentos del usuario o en la de «(defecto)», con la bandeja de la regla.</summary>
    public class ImpresoraPapelPackingServidor : IImpresoraPapelPacking
    {
        private readonly Func<IImpresionDocumentosServidor> impresion;

        public ImpresoraPapelPackingServidor(Func<IImpresionDocumentosServidor> impresion = null)
        {
            this.impresion = impresion ?? (() => new ImpresionDocumentosServidor());
        }

        public string Aplicacion => AplicacionesDocumentoEnPapel.ARIADNA;

        public void Imprimir(List<DocumentoAImprimirEnServidor> documentos, string usuario)
        {
            try
            {
                _ = impresion().Imprimir(documentos, usuario);
            }
            catch (Exception ex)
            {
                foreach (DocumentoAImprimirEnServidor documento in documentos ?? new List<DocumentoAImprimirEnServidor>())
                {
                    documento.Aviso = documento.Aviso ?? $"No se ha podido imprimir {documento.Descripcion}: {ex.Message}";
                }
            }
        }
    }

    /// <summary>NestoAPI#828: Nesto. No imprime aquí: los PDF van en la respuesta y Nesto confirma la impresión.</summary>
    public class ImpresoraPapelPackingLocal : IImpresoraPapelPacking
    {
        public string Aplicacion => AplicacionesDocumentoEnPapel.NESTO;

        public void Imprimir(List<DocumentoAImprimirEnServidor> documentos, string usuario)
        {
            // Los imprime Nesto en local.
        }
    }

    /// <summary>NestoAPI#828: los pedidos de la entrega (o del envío) cuyo papel se saca en el packing.</summary>
    public class PeticionDocumentosEnPapel
    {
        public string Empresa { get; set; }
        /// <summary>El primero es el del envío; los demás, los de la misma entrega en el packing (servir junto).</summary>
        public List<int> Pedidos { get; set; } = new List<int>();
        public int? Envio { get; set; }
        /// <summary>La etiqueta ya se había hecho (se repite): lo ya facturado no se dice como error.</summary>
        public bool Reimpresion { get; set; }
        /// <summary>El usuario resuelto («NUEVAVISION\Andre»): para la impresora de documentos y la auditoría.</summary>
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#828: lo que ha pasado con el papel de los pedidos del envío.</summary>
    public class ResultadoDocumentosEnPapel
    {
        /// <summary>Los documentos en papel: los impresos, los pendientes y los que ya estaban impresos de antes.</summary>
        public List<DocumentoEnPapelDTO> Documentos { get; } = new List<DocumentoEnPapelDTO>();
        /// <summary>Lo que dice el núcleo (pedidos que no se facturan aquí, reimpresiones, fallos).</summary>
        public List<string> Avisos { get; } = new List<string>();
        /// <summary>La respuesta de FacturarPedido de cada pedido facturado aquí (en el orden de la petición).</summary>
        public List<KeyValuePair<int, FacturarRutasResponseDTO>> Respuestas { get; } = new List<KeyValuePair<int, FacturarRutasResponseDTO>>();
        /// <summary>Todo junto (facturas, albaranes, notas, errores y los documentos regenerados al reintentar): lo que imprime Nesto.</summary>
        public FacturarRutasResponseDTO Respuesta { get; } = new FacturarRutasResponseDTO();

        public bool HayPendientes => Documentos.Any(d => d.Estado == EstadosDocumentoEnPapel.PENDIENTE);
    }

    /// <summary>
    /// NestoAPI#828 (sugerencia 973 de Alfredo, decisiones de Carlos del 09/11/26): EL núcleo del papel del packing, el mismo para la
    /// etiqueta de Ariadna (F8, <see cref="Agencias.ServicioEtiquetaPacking"/>) y la de Nesto (POST api/FacturacionRutas/FacturarEtiqueta).
    /// Por cada pedido del envío (todos los de la entrega, una factura por pedido):
    /// <list type="number">
    /// <item>Si su ruta no saca el papel al cerrar el envío (<see cref="ITipoRuta.ImprimeAlCerrarEnvio"/>: la ruta propia) o es una nota
    /// de reparto propio, no se toca: lo hace Facturar rutas.</item>
    /// <item>Si tiene un documento pendiente de imprimir del packing (la impresora falló), se vuelve a imprimir SIN volver a facturar.</item>
    /// <item>Si no, se factura con el núcleo de siempre (FacturarPedido → GestorFacturacionRutas, con la regla del papel de
    /// <see cref="ReglaImpresionDocumentos"/>: solo con la frase de papel, también en las simplificadas).</item>
    /// <item>Cada documento en papel queda marcado «Impreso» o «Pendiente» (dbo.DocumentosPapelPacking); Facturar rutas lo cuenta.</item>
    /// </list>
    /// Nada de esto deshace la etiqueta: lo que falle es un aviso.
    /// </summary>
    public class DocumentosEnPapelDelEnvio
    {
        private readonly IPasosDocumentosEnPapel pasos;
        private readonly IImpresoraPapelPacking impresora;
        private readonly Action<Exception> registrar;

        /// <summary>La ruta → su tipo (TipoRutaFactory). Sustituible en los tests.</summary>
        internal Func<string, ITipoRuta> TipoDeRuta { get; set; } = TipoRutaFactory.ObtenerPorNumeroRuta;

        public DocumentosEnPapelDelEnvio(IPasosDocumentosEnPapel pasos, IImpresoraPapelPacking impresora, Action<Exception> registrar = null)
        {
            this.pasos = pasos ?? throw new ArgumentNullException(nameof(pasos));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>Factura (o reimprime lo pendiente de) cada pedido y marca su papel.</summary>
        public async Task<ResultadoDocumentosEnPapel> Procesar(PeticionDocumentosEnPapel peticion)
        {
            var resultado = new ResultadoDocumentosEnPapel();
            string empresa = EmpresaDe(peticion?.Empresa);
            foreach (int pedido in PedidosDe(peticion))
            {
                if (!await SeSacaEnElPacking(empresa, pedido, resultado))
                {
                    continue;
                }

                List<DocumentoEnPapelDTO> marcas = await LeerMarcasSeguro(empresa, pedido);
                List<DocumentoEnPapelDTO> pendientes = marcas.Where(m => m.Estado == EstadosDocumentoEnPapel.PENDIENTE).ToList();
                if (pendientes.Any())
                {
                    // S3: ya se facturó y el papel no salió: se vuelve a imprimir, sin facturar otra vez
                    await ReimprimirPendientes(empresa, pedido, pendientes, peticion, resultado);
                    continue;
                }

                FacturarRutasResponseDTO respuesta;
                try
                {
                    respuesta = await pasos.Facturar(empresa, pedido);
                }
                catch (Exception ex)
                {
                    Registrar(new Exception($"[NestoAPI#828] Etiqueta del envío {peticion?.Envio} hecha, pero no se pudo facturar el pedido {pedido}: {ex.Message}", ex));
                    resultado.Avisos.Add($"No se ha podido facturar el pedido {pedido}: {ex.Message?.Trim()} Facture desde Nesto.");
                    continue;
                }
                if (respuesta == null)
                {
                    resultado.Respuestas.Add(new KeyValuePair<int, FacturarRutasResponseDTO>(pedido, null));
                    continue;
                }
                resultado.Respuestas.Add(new KeyValuePair<int, FacturarRutasResponseDTO>(pedido, respuesta));
                Juntar(resultado.Respuesta, respuesta);

                List<DocumentoEnPapelDTO> nuevos = await MarcarLosDeLaRespuesta(empresa, pedido, respuesta, peticion);
                resultado.Documentos.AddRange(nuevos);

                // Al repetir la etiqueta, lo que ya salió en el packing se dice (no se vuelve a imprimir)
                if (peticion?.Reimpresion == true && !nuevos.Any())
                {
                    foreach (DocumentoEnPapelDTO impreso in marcas.Where(m => m.Estado == EstadosDocumentoEnPapel.IMPRESO))
                    {
                        impreso.YaEstabaImpreso = true;
                        resultado.Documentos.Add(impreso);
                        resultado.Avisos.Add($"{Mayuscula(impreso.Descripcion)} ya salió en el packing" +
                            $"{(string.IsNullOrWhiteSpace(impreso.Impresora) ? string.Empty : $" (en «{impreso.Impresora.Trim()}»)")}: no se vuelve a imprimir.");
                    }
                }
            }
            return resultado;
        }

        /// <summary>
        /// S3/S6 («Reintentar impresión»): vuelve a imprimir lo pendiente de los pedidos, sin facturar nada. Lo que no está pendiente no
        /// se toca.
        /// </summary>
        public async Task<ResultadoDocumentosEnPapel> ReimprimirPendientes(PeticionDocumentosEnPapel peticion)
        {
            var resultado = new ResultadoDocumentosEnPapel();
            string empresa = EmpresaDe(peticion?.Empresa);
            bool habiaPendientes = false;
            foreach (int pedido in PedidosDe(peticion))
            {
                List<DocumentoEnPapelDTO> pendientes = (await LeerMarcasSeguro(empresa, pedido))
                    .Where(m => m.Estado == EstadosDocumentoEnPapel.PENDIENTE).ToList();
                if (pendientes.Any())
                {
                    habiaPendientes = true;
                    await ReimprimirPendientes(empresa, pedido, pendientes, peticion, resultado);
                }
            }
            if (!habiaPendientes)
            {
                resultado.Avisos.Add(PedidosDe(peticion).Count == 1
                    ? $"El pedido {PedidosDe(peticion)[0]} no tiene ningún documento en papel pendiente de imprimir."
                    : $"Los pedidos {string.Join(", ", PedidosDe(peticion))} no tienen ningún documento en papel pendiente de imprimir.");
            }
            return resultado;
        }

        /// <summary>NestoAPI#828: lo que confirma Nesto tras imprimir en local (o no) un documento del packing.</summary>
        public Task MarcarImpresionLocal(ImpresionDocumentoEnPapelDTO impresion)
        {
            if (impresion == null || impresion.Pedido <= 0 || string.IsNullOrWhiteSpace(impresion.Documento))
            {
                return Task.CompletedTask;
            }
            return MarcarSeguro(new DocumentoEnPapelDTO
            {
                Empresa = EmpresaDe(impresion.Empresa),
                Pedido = impresion.Pedido,
                Envio = impresion.Envio,
                Tipo = string.IsNullOrWhiteSpace(impresion.Tipo) ? TiposDocumentoEnPapel.FACTURA : impresion.Tipo.Trim(),
                Documento = impresion.Documento.Trim(),
                Estado = impresion.Impreso ? EstadosDocumentoEnPapel.IMPRESO : EstadosDocumentoEnPapel.PENDIENTE,
                Impresora = impresion.Impresora,
                Mensaje = impresion.Impreso ? null : (string.IsNullOrWhiteSpace(impresion.Mensaje) ? "Nesto no lo ha podido imprimir." : impresion.Mensaje.Trim())
            });
        }

        // ---------------------------------------------------------------- piezas

        /// <summary>¿El papel de este pedido sale en el packing? Si no, lo dice en los avisos.</summary>
        private async Task<bool> SeSacaEnElPacking(string empresa, int pedido, ResultadoDocumentosEnPapel resultado)
        {
            string ruta;
            try
            {
                ruta = await pasos.RutaDelPedido(empresa, pedido);
            }
            catch (Exception ex)
            {
                // Sin saber la ruta se sigue como hasta ahora (FacturarPedido mira lo suyo)
                Registrar(new Exception($"[NestoAPI#828] No se ha podido leer la ruta del pedido {pedido}: {ex.Message}", ex));
                ruta = null;
            }
            ITipoRuta tipo = string.IsNullOrWhiteSpace(ruta) ? null : TipoDeRuta(ruta.Trim());
            if (tipo != null && !tipo.ImprimeAlCerrarEnvio)
            {
                resultado.Avisos.Add($"El pedido {pedido} es de {tipo.NombreParaMostrar?.Trim().ToLowerInvariant() ?? "una ruta"} ({ruta.Trim()}): " +
                    "no se factura al imprimir la etiqueta. Lo factura Facturar rutas, que imprime sus copias antes de cargar.");
                return false;
            }
            bool esNota;
            try
            {
                esNota = await pasos.EsNotaDeRepartoPropio(empresa, pedido);
            }
            catch (Exception ex)
            {
                // Si no se puede saber, se trata como nota de reparto propio: mejor que la procese Facturar rutas que perder el NE.
                Registrar(new Exception($"[Etiqueta del packing #765] No se ha podido saber si el pedido {pedido} es una nota de reparto propio: no se factura al imprimir. {ex.Message}", ex));
                esNota = true;
            }
            if (esNota)
            {
                resultado.Avisos.Add($"La nota de entrega {pedido} no se procesa al imprimir la etiqueta: la procesa Facturar rutas, que imprime su documento.");
                return false;
            }
            return true;
        }

        /// <summary>Marca los documentos en papel de la respuesta de FacturarPedido (impreso en el servidor o pendiente).</summary>
        private async Task<List<DocumentoEnPapelDTO>> MarcarLosDeLaRespuesta(string empresa, int pedido, FacturarRutasResponseDTO respuesta,
            PeticionDocumentosEnPapel peticion)
        {
            var documentos = new List<DocumentoEnPapelDTO>();
            List<PedidoConErrorDTO> fallosImpresion = (respuesta.PedidosConErrores ?? new List<PedidoConErrorDTO>())
                .Where(e => e?.TipoError == ImpresionDocumentosServidor.TIPO_ERROR).ToList();
            foreach (KeyValuePair<DocumentoImprimibleDTO, DocumentoEnPapelDTO> par in EnPapel(respuesta, empresa, pedido, peticion?.Envio))
            {
                DocumentoParaImprimir datos = par.Key.DatosImpresion;
                DocumentoEnPapelDTO documento = par.Value;
                if (!string.IsNullOrWhiteSpace(datos.ImpresoEnServidor))
                {
                    documento.Estado = EstadosDocumentoEnPapel.IMPRESO;
                    documento.Impresora = datos.ImpresoEnServidor.Trim();
                }
                else
                {
                    documento.Estado = EstadosDocumentoEnPapel.PENDIENTE;
                    documento.Mensaje = impresora.Aplicacion == AplicacionesDocumentoEnPapel.NESTO
                        ? "Lo imprime Nesto."
                        : fallosImpresion.FirstOrDefault(e => e.MensajeError != null && e.MensajeError.Contains(documento.Documento))?.MensajeError
                            ?? fallosImpresion.FirstOrDefault()?.MensajeError
                            ?? "No ha salido por la impresora del servidor.";
                }
                await MarcarSeguro(documento);
                documentos.Add(documento);
            }
            return documentos;
        }

        private async Task ReimprimirPendientes(string empresa, int pedido, List<DocumentoEnPapelDTO> pendientes, PeticionDocumentosEnPapel peticion,
            ResultadoDocumentosEnPapel resultado)
        {
            foreach (DocumentoEnPapelDTO marca in pendientes)
            {
                marca.Envio = marca.Envio ?? peticion?.Envio;
                DocumentosImpresionPedidoDTO regenerados;
                try
                {
                    regenerados = await pasos.Regenerar(EmpresaDe(marca.Empresa ?? empresa), pedido, marca.Tipo, marca.Documento);
                }
                catch (Exception ex)
                {
                    Registrar(new Exception($"[NestoAPI#828] No se ha podido volver a generar {marca.Descripcion} del pedido {pedido}: {ex.Message}", ex));
                    marca.Mensaje = $"No se ha podido volver a generar: {ex.Message?.Trim()}";
                    resultado.Documentos.Add(marca);
                    resultado.Avisos.Add($"No se ha podido volver a imprimir {marca.Descripcion} del pedido {pedido}: {ex.Message?.Trim()} Imprímala desde Nesto.");
                    continue;
                }

                var paraImprimir = new List<DocumentoAImprimirEnServidor>();
                foreach (DocumentoImprimibleDTO documento in Documentos(regenerados).Where(d => d.DatosImpresion != null && d.DatosImpresion.NumeroCopias > 0))
                {
                    paraImprimir.Add(new DocumentoAImprimirEnServidor { Descripcion = marca.Descripcion, Datos = documento.DatosImpresion, Documento = documento });
                }
                if (!paraImprimir.Any())
                {
                    // Ya no lleva papel (le han quitado la frase): no se queda pendiente para siempre
                    marca.Estado = EstadosDocumentoEnPapel.IMPRESO;
                    marca.Mensaje = "Ya no lleva documento en papel.";
                    await MarcarSeguro(marca);
                    resultado.Avisos.Add($"{Mayuscula(marca.Descripcion)} del pedido {pedido} ya no lleva documento en papel: no se imprime.");
                    continue;
                }

                impresora.Imprimir(paraImprimir, peticion?.Usuario);
                foreach (DocumentoAImprimirEnServidor documento in paraImprimir)
                {
                    if (documento.Documento is DocumentoImprimibleDTO imprimible)
                    {
                        Juntar(resultado.Respuesta, imprimible);
                    }
                }
                DocumentoAImprimirEnServidor primero = paraImprimir[0];
                bool impreso = !string.IsNullOrWhiteSpace(primero.Datos.ImpresoEnServidor);
                marca.Reimpreso = true;
                marca.Estado = impreso ? EstadosDocumentoEnPapel.IMPRESO : EstadosDocumentoEnPapel.PENDIENTE;
                marca.Impresora = impreso ? primero.Datos.ImpresoEnServidor.Trim() : marca.Impresora;
                marca.Mensaje = impreso ? null
                    : impresora.Aplicacion == AplicacionesDocumentoEnPapel.NESTO ? "Lo imprime Nesto."
                    : primero.Aviso ?? "No ha salido por la impresora del servidor.";
                await MarcarSeguro(marca);
                resultado.Documentos.Add(marca);
                if (impresora.Aplicacion != AplicacionesDocumentoEnPapel.NESTO)
                {
                    resultado.Avisos.Add(impreso
                        ? $"{Mayuscula(marca.Descripcion)} del pedido {pedido} se ha vuelto a imprimir en «{marca.Impresora}» (sin volver a facturar)."
                        : $"{Mayuscula(marca.Descripcion)} del pedido {pedido} sigue sin imprimirse: {marca.Mensaje}");
                }
            }
        }

        /// <summary>Los documentos de la respuesta que llevan papel (DatosImpresion con copias), con su marca sin estado.</summary>
        internal static List<KeyValuePair<DocumentoImprimibleDTO, DocumentoEnPapelDTO>> EnPapel(FacturarRutasResponseDTO respuesta, string empresa, int pedido, int? envio)
        {
            var lista = new List<KeyValuePair<DocumentoImprimibleDTO, DocumentoEnPapelDTO>>();
            if (respuesta == null)
            {
                return lista;
            }
            foreach (FacturaCreadaDTO f in respuesta.Facturas ?? new List<FacturaCreadaDTO>())
            {
                Anadir(lista, f, TiposDocumentoEnPapel.FACTURA, f?.NumeroFactura, empresa, pedido, envio);
            }
            foreach (AlbaranCreadoDTO a in respuesta.Albaranes ?? new List<AlbaranCreadoDTO>())
            {
                Anadir(lista, a, TiposDocumentoEnPapel.ALBARAN, a?.NumeroAlbaran.ToString(), empresa, pedido, envio);
            }
            foreach (NotaEntregaCreadaDTO n in respuesta.NotasEntrega ?? new List<NotaEntregaCreadaDTO>())
            {
                Anadir(lista, n, TiposDocumentoEnPapel.NOTA_ENTREGA, (n != null && n.NumeroPedido > 0 ? n.NumeroPedido : pedido).ToString(), empresa, pedido, envio);
            }
            return lista;
        }

        private static void Anadir(List<KeyValuePair<DocumentoImprimibleDTO, DocumentoEnPapelDTO>> lista, DocumentoImprimibleDTO documento, string tipo,
            string numero, string empresa, int pedido, int? envio)
        {
            if (documento?.DatosImpresion == null || documento.DatosImpresion.NumeroCopias <= 0 || string.IsNullOrWhiteSpace(numero))
            {
                return;
            }
            lista.Add(new KeyValuePair<DocumentoImprimibleDTO, DocumentoEnPapelDTO>(documento, new DocumentoEnPapelDTO
            {
                Empresa = string.IsNullOrWhiteSpace(documento.Empresa) ? empresa : documento.Empresa.Trim(),
                Pedido = documento.NumeroPedido > 0 ? documento.NumeroPedido : pedido,
                Envio = envio,
                Tipo = tipo,
                Documento = numero.Trim(),
                Fecha = DateTime.Now
            }));
        }

        private static IEnumerable<DocumentoImprimibleDTO> Documentos(DocumentosImpresionPedidoDTO documentos)
            => documentos == null
                ? Enumerable.Empty<DocumentoImprimibleDTO>()
                : (documentos.Facturas ?? new List<FacturaCreadaDTO>()).Cast<DocumentoImprimibleDTO>()
                    .Concat(documentos.Albaranes ?? new List<AlbaranCreadoDTO>())
                    .Concat(documentos.NotasEntrega ?? new List<NotaEntregaCreadaDTO>())
                    .Where(d => d != null);

        private static void Juntar(FacturarRutasResponseDTO total, FacturarRutasResponseDTO parte)
        {
            total.PedidosProcesados += parte.PedidosProcesados;
            total.Facturas.AddRange(parte.Facturas ?? new List<FacturaCreadaDTO>());
            total.Albaranes.AddRange(parte.Albaranes ?? new List<AlbaranCreadoDTO>());
            total.NotasEntrega.AddRange(parte.NotasEntrega ?? new List<NotaEntregaCreadaDTO>());
            total.PedidosConErrores.AddRange(parte.PedidosConErrores ?? new List<PedidoConErrorDTO>());
            total.ReembolsosDesfasados.AddRange(parte.ReembolsosDesfasados ?? new List<ReembolsoDesfasadoDTO>());
            total.CartasChequeRegalo.AddRange(parte.CartasChequeRegalo ?? new List<CartaChequeRegaloDTO>());
            total.TiempoTotal += parte.TiempoTotal;
        }

        private static void Juntar(FacturarRutasResponseDTO total, DocumentoImprimibleDTO documento)
        {
            switch (documento)
            {
                case FacturaCreadaDTO f:
                    total.Facturas.Add(f);
                    break;
                case AlbaranCreadoDTO a:
                    total.Albaranes.Add(a);
                    break;
                case NotaEntregaCreadaDTO n:
                    total.NotasEntrega.Add(n);
                    break;
            }
        }

        private async Task<List<DocumentoEnPapelDTO>> LeerMarcasSeguro(string empresa, int pedido)
        {
            try
            {
                return await pasos.LeerMarcas(empresa, pedido) ?? new List<DocumentoEnPapelDTO>();
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[NestoAPI#828] No se han podido leer las marcas del papel del pedido {pedido}: {ex.Message}", ex));
                return new List<DocumentoEnPapelDTO>();
            }
        }

        private async Task MarcarSeguro(DocumentoEnPapelDTO marca)
        {
            try
            {
                await pasos.Marcar(marca, impresora.Aplicacion);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[NestoAPI#828] No se ha podido marcar {marca?.Descripcion} del pedido {marca?.Pedido}: {ex.Message}", ex));
            }
        }

        private static List<int> PedidosDe(PeticionDocumentosEnPapel peticion)
            => (peticion?.Pedidos ?? new List<int>()).Where(p => p > 0).Distinct().ToList();

        private static string EmpresaDe(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        internal static string Mayuscula(string texto)
            => string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // El registro nunca rompe nada.
            }
        }
    }
}
