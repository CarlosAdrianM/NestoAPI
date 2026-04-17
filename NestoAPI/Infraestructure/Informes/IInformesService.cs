using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models.Informes;

namespace NestoAPI.Infraestructure.Informes
{
    public interface IInformesService
    {
        Task<List<ResumenVentasDTO>> LeerResumenVentasAsync(DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas);
        Task<List<ControlPedidosDTO>> LeerControlPedidosAsync();
        Task<List<DetalleRapportsDTO>> LeerDetalleRapportsAsync(DateTime fechaDesde, DateTime fechaHasta, string listaVendedores);
        Task<List<ExtractoContableDTO>> LeerExtractoContableAsync(string empresa, string cuenta, DateTime fechaDesde, DateTime fechaHasta);
        Task<List<UbicacionesInventarioDTO>> LeerUbicacionesInventarioAsync(string empresa);
        Task<List<KitsQueSePuedenMontarDTO>> LeerKitsQueSePuedenMontarAsync(string empresa, string fecha, string almacen, string filtroRutas);
        Task<List<MontarKitProductosDTO>> LeerMontarKitProductosAsync(int traspaso);
        Task<List<ManifiestoAgenciaDTO>> LeerManifiestoAgenciaAsync(string empresa, int agencia, DateTime fecha);
        Task<PedidoCompraInformeDTO> LeerPedidoCompraAsync(string empresa, int pedido);
    }
}
