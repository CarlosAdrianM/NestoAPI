using NestoAPI.Models;
using NestoAPI.Models.ApuntesBanco;
using NestoAPI.Models.Bancos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public interface IContabilidadService
    {
        Task ContabilizarComisionesTarjetas(List<MovimientoTPVDTO> movimientosTPV);
        Task<int> ContabilizarDiario(string empresa, string diario, string usuario);
        Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario, string usuario);
        Task<int> CrearLineas(List<PreContabilidad> lineas);
        Task<int> CrearLineas(NVEntities db, List<PreContabilidad> lineas);
        Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas);
        Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas, NVEntities db);
        Task<string> LeerProveedorPorNombre(string nombreProveedor);
        Task<ExtractoProveedorDTO> PagoPendienteUnico(string proveedor, decimal importe);
        Task<bool> PersistirCuaderno43(ContenidoCuaderno43 contenido);
        Task<bool> PersistirMovimientosTPV(List<MovimientoTPVDTO> movimientosTPV);
        Task<bool> PuntearPorImporte(string empresa, string cuenta, decimal importe);
        Task<int> NumeroRecibosRemesa(int remesa);
        Task<decimal> SaldoFinal(string entidad, string oficina, string cuenta, DateTime fecha);
        Task<decimal> SaldoInicial(string entidad, string oficina, string cuenta, DateTime fecha);
        
    }
}
