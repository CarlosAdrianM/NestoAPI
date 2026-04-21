using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models.Informes;
using NestoAPI.Models.Informes.SaldoCuenta555;

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
        Task<List<PickingDTO>> LeerPickingAsync(int picking, string empresa = "1", int personas = 1);
        Task<int> LeerUltimoPickingAsync();
        Task<List<PackingDTO>> LeerPackingAsync(int picking, int personas = 1);
        Task<List<ManifiestoAgenciaDTO>> LeerManifiestoAgenciaAsync(string empresa, int agencia, DateTime fecha);
        Task<PedidoCompraInformeDTO> LeerPedidoCompraAsync(string empresa, int pedido);
        Task<List<ExtractoProveedorDTO>> LeerExtractoProveedorAsync(string empresa, string proveedor, DateTime fechaDesde, DateTime fechaHasta);
        Task<SaldoCuenta555ResultadoDto> LeerSaldoCuenta555Async(string empresa, string cuenta, DateTime fechaCorte);
    }
}
