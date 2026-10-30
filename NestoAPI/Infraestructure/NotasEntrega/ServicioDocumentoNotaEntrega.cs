using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#758: el PDF de la nota, o por qué no.</summary>
    public class ResultadoDocumentoNotaEntrega
    {
        public TipoResultadoNotaEntrega Tipo { get; private set; }
        public string Mensaje { get; private set; }
        public byte[] Pdf { get; private set; }
        /// <summary>NE{pedido} / NR{pedido}: el nombre del fichero.</summary>
        public string CodigoDocumento { get; private set; }

        public static ResultadoDocumentoNotaEntrega Ok(byte[] pdf, string codigo)
            => new ResultadoDocumentoNotaEntrega { Tipo = TipoResultadoNotaEntrega.Ok, Pdf = pdf, CodigoDocumento = codigo };
        public static ResultadoDocumentoNotaEntrega NoEncontrado(string mensaje)
            => new ResultadoDocumentoNotaEntrega { Tipo = TipoResultadoNotaEntrega.NoEncontrado, Mensaje = mensaje };
        public static ResultadoDocumentoNotaEntrega Invalido(string mensaje)
            => new ResultadoDocumentoNotaEntrega { Tipo = TipoResultadoNotaEntrega.Invalido, Mensaje = mensaje };
    }

    public interface IServicioDocumentoNotaEntrega
    {
        /// <summary>
        /// GET api/NotasEntrega/{empresa}/{pedido}/Documento: el PDF de la nota (también antes de facturar la ruta y para las de
        /// Nesto viejo). No crea la parada.
        /// </summary>
        Task<ResultadoDocumentoNotaEntrega> Generar(string empresa, int pedido, string usuario, bool papelConMembrete);

        /// <summary>El documento del pedido con los datos de la nota (Facturar rutas lo imprime con sus copias y bandeja).</summary>
        Task<Factura> LeerParaImprimir(string empresa, int pedido);

        /// <summary>El PDF de un documento ya leído, siempre con QuestPDF (el único que dibuja el Code128).</summary>
        byte[] Pdf(Factura factura, bool papelConMembrete, string usuario);
    }

    /// <summary>
    /// NestoAPI#758 (corte 2 de #755): el documento de una nota de entrega o de recogida. Es el del pedido
    /// (<see cref="IGestorFacturas.LeerPedido"/>) más título por tipo («NOTA DE ENTREGA» / «NOTA DE RECOGIDA»), número de nota,
    /// bultos, teléfono y móvil del contacto, las líneas de texto en grande, «Recibí» y el Code128 NE{Pedido} / NR{Pedido}
    /// (<see cref="GeneradorPdfFacturasQuestPdf"/>).
    /// </summary>
    public class ServicioDocumentoNotaEntrega : IServicioDocumentoNotaEntrega
    {
        private readonly IGestorFacturas gestorFacturas;
        private readonly IRepositorioNotasEntregaCabecera cabeceras;
        private readonly IRepositorioParadasNotaEntrega paradas;

        public ServicioDocumentoNotaEntrega(NVEntities db, IGestorFacturas gestorFacturas)
            : this(gestorFacturas, new RepositorioNotasEntregaCabecera(db), new RepositorioParadasNotaEntrega(db))
        {
        }

        public ServicioDocumentoNotaEntrega(IGestorFacturas gestorFacturas, IRepositorioNotasEntregaCabecera cabeceras, IRepositorioParadasNotaEntrega paradas)
        {
            this.gestorFacturas = gestorFacturas ?? throw new ArgumentNullException(nameof(gestorFacturas));
            this.cabeceras = cabeceras ?? throw new ArgumentNullException(nameof(cabeceras));
            this.paradas = paradas ?? throw new ArgumentNullException(nameof(paradas));
        }

        public async Task<ResultadoDocumentoNotaEntrega> Generar(string empresa, int pedido, string usuario, bool papelConMembrete)
        {
            empresa = empresa?.Trim();
            PedidoNotaEntrega cabecera = await cabeceras.LeerPedido(empresa, pedido).ConfigureAwait(false);
            if (cabecera == null)
            {
                return ResultadoDocumentoNotaEntrega.NoEncontrado($"No existe el pedido {pedido}.");
            }
            if (!cabecera.NotaEntrega)
            {
                return ResultadoDocumentoNotaEntrega.NoEncontrado($"El pedido {pedido} no es una nota de entrega.");
            }
            Factura factura;
            try
            {
                factura = await LeerParaImprimir(empresa, pedido).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // LeerPedido explica sus motivos (p. ej. «no tiene ninguna línea»).
                return ResultadoDocumentoNotaEntrega.Invalido($"No se puede imprimir la nota {pedido}: {ex.Message}");
            }
            return ResultadoDocumentoNotaEntrega.Ok(Pdf(factura, papelConMembrete, usuario), factura.NotaEntrega.CodigoBarras);
        }

        public async Task<Factura> LeerParaImprimir(string empresa, int pedido)
        {
            empresa = empresa?.Trim();
            Factura factura = gestorFacturas.LeerPedido(empresa, pedido);
            FilaNotaEntregaCabecera fila = await cabeceras.Leer(empresa, pedido).ConfigureAwait(false);
            string tipo = TiposNotaEntrega.Normalizar(fila?.Tipo) ?? TiposNotaEntrega.ENTREGA;
            short? bultos;
            if (tipo == TiposNotaEntrega.RECOGIDA)
            {
                bultos = (short)Math.Max(1, (int)(fila?.Bultos ?? TiposNotaEntrega.BULTOS_POR_DEFECTO));
            }
            else
            {
                // Los de una entrega salen del packing: los de su envío (el más nuevo que no esté anulado o pendiente).
                List<EnvioPedidoNota> envios = await paradas.LeerEnviosDelPedido(pedido).ConfigureAwait(false) ?? new List<EnvioPedidoNota>();
                bultos = envios.FirstOrDefault(e => e.Estado >= Constantes.Agencias.ESTADO_EN_CURSO && e.Estado != Constantes.Agencias.ESTADO_DEVUELTO)?.Bultos;
            }
            DireccionFactura entrega = factura.Direcciones?.FirstOrDefault(d => d.Tipo == "Entrega");
            var telefono = new Telefono(entrega?.Telefonos);
            factura.TipoDocumento = Constantes.Facturas.TiposDocumento.NOTA_ENTREGA;
            factura.NotaEntrega = new DatosNotaEntregaFactura
            {
                Tipo = tipo,
                Titulo = TiposNotaEntrega.Titulo(tipo),
                CodigoBarras = TiposNotaEntrega.CodigoDocumento(tipo, pedido),
                NumeroNota = await cabeceras.LeerNumeroNota(empresa, pedido).ConfigureAwait(false),
                Bultos = bultos,
                Telefono = telefono.FijoUnico(),
                Movil = telefono.MovilUnico()
            };
            return factura;
        }

        public byte[] Pdf(Factura factura, bool papelConMembrete, string usuario)
        {
            using (System.Net.Http.ByteArrayContent contenido = gestorFacturas.FacturasEnPDF(new List<Factura> { factura }, papelConMembrete, usuario,
                mostrarImagenes: false, forzarQuestPdf: true))
            {
                return contenido.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
        }
    }
}
