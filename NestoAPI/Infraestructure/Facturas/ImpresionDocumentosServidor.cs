using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#806: interruptor Facturacion:ImprimirDocumentosEnServidor. Solo "true" lo enciende; apagado, como hasta ahora: la
    /// API devuelve los DatosImpresion y cada cliente imprime (o no) a su manera.
    /// </summary>
    public static class InterruptorImpresionDocumentosServidor
    {
        public const string CLAVE = "Facturacion:ImprimirDocumentosEnServidor";

        /// <summary>Lee el Web.config en cada llamada.</summary>
        public static bool EstaActivo() => EstaActivo(ConfigurationManager.AppSettings[CLAVE]);

        public static bool EstaActivo(string valor)
            => string.Equals(valor?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>NestoAPI#806: lo que se apunta en ELMAH de cada impresión en el servidor (información, no un error).</summary>
    public class ImpresionDocumentoServidorInfo : Exception
    {
        public ImpresionDocumentoServidorInfo(string mensaje, Exception interna = null) : base(mensaje, interna) { }
    }

    /// <summary>NestoAPI#806: un documento que puede salir por la impresora del servidor.</summary>
    public class DocumentoAImprimirEnServidor
    {
        /// <summary>Para los avisos: «la factura NV2617700», «el albarán 123456», «la nota de entrega del pedido 929244».</summary>
        public string Descripcion { get; set; }

        public DocumentoParaImprimir Datos { get; set; }

        /// <summary>El documento de la respuesta (empresa, pedido y cliente para el aviso). Opcional.</summary>
        public DocumentoCreadoDTO Documento { get; set; }

        /// <summary>Lo rellena la impresión si no se ha podido imprimir.</summary>
        public string Aviso { get; set; }
    }

    public interface IImpresionDocumentosServidor
    {
        /// <summary>
        /// Imprime las facturas, albaranes y notas de entrega de la respuesta que llevan DatosImpresion; los que no se han podido
        /// imprimir van a PedidosConErrores como aviso (Warning). Con el interruptor apagado no hace nada. Nunca lanza.
        /// </summary>
        void Imprimir(FacturarRutasResponseDTO respuesta, string usuario);

        /// <summary>Imprime los documentos y devuelve los avisos de los que no se han podido imprimir. Nunca lanza.</summary>
        List<string> Imprimir(IEnumerable<DocumentoAImprimirEnServidor> documentos, string usuario);
    }

    /// <summary>
    /// NestoAPI#806 (sugerencia 973 de Alfredo): la API imprime EN EL SERVIDOR el documento en papel al facturar, por cualquier camino,
    /// en la impresora de documentos del usuario (parámetro de usuario <see cref="PARAMETRO_IMPRESORA"/>, o la de «(defecto)», la de
    /// Algete), con las copias y la bandeja que ya decide la API (<see cref="ReglaImpresionDocumentos"/>). Apunta en ELMAH qué
    /// se imprimió y dónde; si no hay impresora o falla, la facturación sigue: solo un aviso. Los DatosImpresion siguen en la
    /// respuesta (con ImpresoEnServidor), para que el cliente decida si deja de imprimir.
    /// <para>NestoAPI#834 (paso 2): con el interruptor encendido el servidor imprime para TODOS los usuarios, también los de Nesto
    /// (desde Nesto 1.10.56, Nesto#620, Nesto respeta ImpresoEnServidor y no vuelve a imprimir en local). Un Nesto anterior
    /// (1.10.55 o menos) imprimiría dos veces: compruébese antes con GET api/Notificaciones/ConectadosNesto.</para>
    /// <para><b>SEGURIDAD DEL DESPLIEGUE (NestoAPI#834)</b>: la fila «(defecto)» de <see cref="PARAMETRO_IMPRESORA"/> es la impresora
    /// de ALGETE. Quien no tenga su propia fila imprime en Algete. ANTES de publicar este cambio, cada usuario de tienda (Reina,
    /// Alcobendas) y de Administración tiene que tener su fila <see cref="PARAMETRO_IMPRESORA"/> en ParametrosUsuario (sin dominio:
    /// «Alfredo», «Reina»), con el script Scripts/Issue834_ImpresoraDocumentosPorUsuario.sql. La fila del usuario se lee SIN
    /// copiar la de «(defecto)» (<see cref="ILectorParametrosUsuario.LeerParametroSinDefecto"/>), para que quien no la tenga no se
    /// quede con la de Algete grabada como suya.</para>
    /// </summary>
    public class ImpresionDocumentosServidor : IImpresionDocumentosServidor
    {
        public const string PARAMETRO_IMPRESORA = "ImpresoraDocumentos";
        public const string USUARIO_DEFECTO = "(defecto)";
        /// <summary>
        /// Carlos 04/11/26: el parámetro también admite este valor, que significa «la impresora predeterminada de Windows en el
        /// servidor» (la de RDS2016), sin tener que escribir su nombre.
        /// </summary>
        public const string IMPRESORA_PREDETERMINADA = "(predeterminada)";

        /// <summary>¿El valor del parámetro pide la impresora predeterminada del servidor?</summary>
        public static bool EsPredeterminada(string impresora)
            => !string.IsNullOrWhiteSpace(impresora) && string.Equals(impresora.Trim(), IMPRESORA_PREDETERMINADA, StringComparison.OrdinalIgnoreCase);
        public const string TIPO_ERROR = "Impresión en servidor";

        private readonly ILectorParametrosUsuario lector;
        private readonly IImpresoraDocumentos impresora;
        private readonly Func<bool> activo;
        private readonly Action<Exception> registrar;

        public ImpresionDocumentosServidor()
            : this(new LectorParametrosUsuario(), new ImpresoraDocumentosPdfium(), InterruptorImpresionDocumentosServidor.EstaActivo, ElmahHelper.Log)
        {
        }

        internal ImpresionDocumentosServidor(ILectorParametrosUsuario lector, IImpresoraDocumentos impresora, Func<bool> activo,
            Action<Exception> registrar = null)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
            this.activo = activo ?? (() => false);
            this.registrar = registrar ?? (_ => { });
        }

        /// <summary>
        /// La impresora de documentos: la del usuario si la tiene; si no, la de «(defecto)» (Algete); si tampoco, null.
        /// <para>NestoAPI#834: el usuario llega resuelto («NUEVAVISION\Alfredo», también desde Ariadna) y Nesto guarda
        /// ParametrosUsuario sin dominio («Alfredo», «Reina»): se busca sin dominio. Solo la fila propia (sin copiar la de
        /// «(defecto)» como hace LeerParametro).</para>
        /// </summary>
        public string ElegirImpresora(string usuario)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string sinDominio = UsuarioAuditoriaHelper.SinDominio(usuario);
            if (!string.IsNullOrWhiteSpace(sinDominio))
            {
                string propia = lector.LeerParametroSinDefecto(empresa, sinDominio, PARAMETRO_IMPRESORA);
                if (!string.IsNullOrWhiteSpace(propia))
                {
                    return propia.Trim();
                }
            }
            string porDefecto = lector.LeerParametroSinDefecto(empresa, USUARIO_DEFECTO, PARAMETRO_IMPRESORA);
            return string.IsNullOrWhiteSpace(porDefecto) ? null : porDefecto.Trim();
        }

        public void Imprimir(FacturarRutasResponseDTO respuesta, string usuario)
        {
            if (respuesta == null || !EstaActivo())
            {
                return;
            }
            try
            {
                var documentos = new List<DocumentoAImprimirEnServidor>();
                documentos.AddRange((respuesta.Facturas ?? new List<FacturaCreadaDTO>()).Select(f => new DocumentoAImprimirEnServidor
                {
                    Descripcion = $"la factura {f.NumeroFactura?.Trim()}",
                    Datos = f.DatosImpresion,
                    Documento = f
                }));
                documentos.AddRange((respuesta.Albaranes ?? new List<AlbaranCreadoDTO>()).Select(a => new DocumentoAImprimirEnServidor
                {
                    Descripcion = $"el albarán {a.NumeroAlbaran}",
                    Datos = a.DatosImpresion,
                    Documento = a
                }));
                documentos.AddRange((respuesta.NotasEntrega ?? new List<NotaEntregaCreadaDTO>()).Select(n => new DocumentoAImprimirEnServidor
                {
                    Descripcion = $"la nota de entrega del pedido {n.NumeroPedido}",
                    Datos = n.DatosImpresion,
                    Documento = n
                }));

                _ = Imprimir(documentos, usuario);

                if (respuesta.PedidosConErrores == null)
                {
                    respuesta.PedidosConErrores = new List<PedidoConErrorDTO>();
                }
                foreach (DocumentoAImprimirEnServidor documento in documentos.Where(d => !string.IsNullOrWhiteSpace(d.Aviso)))
                {
                    respuesta.PedidosConErrores.Add(new PedidoConErrorDTO
                    {
                        Empresa = documento.Documento?.Empresa?.Trim(),
                        NumeroPedido = documento.Documento?.NumeroPedido ?? 0,
                        Cliente = documento.Documento?.Cliente?.Trim(),
                        Contacto = documento.Documento?.Contacto?.Trim(),
                        NombreCliente = documento.Documento?.NombreCliente?.Trim(),
                        TipoError = TIPO_ERROR,
                        MensajeError = documento.Aviso,
                        Severidad = NivelSeveridad.Warning,
                        FechaEntrega = DateTime.Today
                    });
                }
            }
            catch (Exception ex)
            {
                // La impresión NUNCA rompe la facturación.
                Registrar(new ImpresionDocumentoServidorInfo($"[NestoAPI#806] Fallo al imprimir en el servidor los documentos de la facturación: {ex.Message}", ex));
            }
        }

        public List<string> Imprimir(IEnumerable<DocumentoAImprimirEnServidor> documentos, string usuario)
        {
            var avisos = new List<string>();
            if (documentos == null || !EstaActivo())
            {
                return avisos;
            }
            List<DocumentoAImprimirEnServidor> paraImprimir = documentos
                .Where(d => d?.Datos != null && d.Datos.NumeroCopias > 0 && d.Datos.BytesPDF != null && d.Datos.BytesPDF.Length > 0)
                .ToList();
            if (!paraImprimir.Any())
            {
                return avisos;
            }

            string nombreImpresora;
            try
            {
                nombreImpresora = ElegirImpresora(usuario);
            }
            catch (Exception ex)
            {
                foreach (DocumentoAImprimirEnServidor documento in paraImprimir)
                {
                    documento.Aviso = $"No se ha podido imprimir {documento.Descripcion}: no se ha podido leer la impresora de documentos ({PARAMETRO_IMPRESORA}): {ex.Message?.Trim().TrimEnd('.')}. Imprímalo desde Nesto.";
                    avisos.Add(documento.Aviso);
                }
                Registrar(new ImpresionDocumentoServidorInfo($"[NestoAPI#806] No se ha podido leer el parámetro {PARAMETRO_IMPRESORA} de {usuario}: {ex.Message}", ex));
                return avisos;
            }

            if (nombreImpresora == null)
            {
                foreach (DocumentoAImprimirEnServidor documento in paraImprimir)
                {
                    documento.Aviso = $"No se ha podido imprimir {documento.Descripcion}: sin impresora de documentos (parámetro {PARAMETRO_IMPRESORA}). Imprímalo desde Nesto.";
                    avisos.Add(documento.Aviso);
                }
                Registrar(new ImpresionDocumentoServidorInfo($"[NestoAPI#806] Sin impresora de documentos (parámetro {PARAMETRO_IMPRESORA}) para {usuario} ni en «{USUARIO_DEFECTO}»: no se han impreso {string.Join(", ", paraImprimir.Select(d => d.Descripcion))}."));
                return avisos;
            }

            foreach (DocumentoAImprimirEnServidor documento in paraImprimir)
            {
                DocumentoParaImprimir datos = documento.Datos;
                try
                {
                    impresora.Imprimir(datos.BytesPDF, nombreImpresora, datos.NumeroCopias, datos.TipoBandeja);
                    datos.ImpresoEnServidor = nombreImpresora;
                    Registrar(new ImpresionDocumentoServidorInfo($"[NestoAPI#806] Impreso en el servidor {documento.Descripcion}: {datos.NumeroCopias} copia(s), bandeja {datos.TipoBandeja}, en «{nombreImpresora}» (usuario {usuario})."));
                }
                catch (Exception ex)
                {
                    documento.Aviso = $"No se ha podido imprimir {documento.Descripcion} en «{nombreImpresora}»: {ex.Message?.Trim().TrimEnd('.')}. Imprímalo desde Nesto.";
                    avisos.Add(documento.Aviso);
                    Registrar(new ImpresionDocumentoServidorInfo($"[NestoAPI#806] No se ha podido imprimir {documento.Descripcion} en «{nombreImpresora}» (usuario {usuario}): {ex.Message}", ex));
                }
            }
            return avisos;
        }

        private bool EstaActivo()
        {
            try
            {
                return activo();
            }
            catch
            {
                return false;
            }
        }

        private void Registrar(Exception info)
        {
            try
            {
                registrar(info);
            }
            catch
            {
                // El registro nunca rompe nada.
            }
        }
    }
}
