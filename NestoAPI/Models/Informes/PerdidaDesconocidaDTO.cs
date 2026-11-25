using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Informes
{
    /// <summary>
    /// NestoAPI#893: una fila de la consulta agregada de ExtractoProducto (almacén, mes, producto y diario), sin limpiar. Solo lectura.
    /// <see cref="Bajas"/> en positivo. <see cref="PrecioMedio"/> = Productos.PrecioMedio de HOY (como IndicadoresStock).
    /// </summary>
    public class FilaPerdidaDesconocidaDTO
    {
        public string Almacen { get; set; }
        public int Mes { get; set; }
        public string Producto { get; set; }
        public string Diario { get; set; }
        public int Altas { get; set; }
        public int Bajas { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public decimal? PrecioMedio { get; set; }
    }

    /// <summary>NestoAPI#893: lo que falta y lo que sobra de un almacén en un mes (o en el acumulado del año, <see cref="Mes"/> = 0), a coste.</summary>
    public class PeriodoPerdidaDesconocidaDTO
    {
        /// <summary>1..12; 0 = acumulado desde el 1 de enero.</summary>
        public int Mes { get; set; }
        /// <summary>Suma, producto a producto, de lo que ha quedado en negativo (en positivo).</summary>
        public int FaltaUnidades { get; set; }
        public decimal FaltaValor { get; set; }
        /// <summary>Suma, producto a producto, de lo que ha quedado en positivo.</summary>
        public int SobraUnidades { get; set; }
        public decimal SobraValor { get; set; }
        /// <summary>Lo que falta aquí y sobra en otro almacén (mismo producto y mismo periodo).</summary>
        public int FaltaCuadradaUnidades { get; set; }
        public decimal FaltaCuadradaValor { get; set; }
        /// <summary>Lo que sobra aquí y falta en otro almacén (mismo producto y mismo periodo).</summary>
        public int SobraCuadradaUnidades { get; set; }
        public decimal SobraCuadradaValor { get; set; }
        /// <summary>Falta − falta cuadrada: la pérdida de verdad.</summary>
        public int PerdidaRealUnidades { get; set; }
        public decimal PerdidaRealValor { get; set; }
        /// <summary>Sobra − sobra cuadrada.</summary>
        public int SobraRealUnidades { get; set; }
        public decimal SobraRealValor { get; set; }
        /// <summary>Sobra − falta (con signo).</summary>
        public int NetoUnidades { get; set; }
        public decimal NetoValor { get; set; }
    }

    /// <summary>NestoAPI#893: un almacén (ALG, REI, ALC) mes a mes y su acumulado.</summary>
    public class AlmacenPerdidaDesconocidaDTO
    {
        public string Almacen { get; set; }
        public List<PeriodoPerdidaDesconocidaDTO> Meses { get; set; } = new List<PeriodoPerdidaDesconocidaDTO>();
        public PeriodoPerdidaDesconocidaDTO Acumulado { get; set; }
    }

    /// <summary>NestoAPI#893: un producto de un almacén en el acumulado del año (solo los que no quedan a cero o tienen alta y baja).</summary>
    public class ProductoPerdidaDesconocidaDTO
    {
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public decimal PrecioMedio { get; set; }
        public int UnidadesAlta { get; set; }
        /// <summary>En positivo.</summary>
        public int UnidadesBaja { get; set; }
        public int UnidadesNeto { get; set; }
        public decimal ValorNeto { get; set; }
        /// <summary>Unidades (en positivo) de su falta o su sobra que cuadran con otro almacén.</summary>
        public int UnidadesCuadradas { get; set; }
        /// <summary>Con quién cuadra («REI», «ALC»…).</summary>
        public List<string> CuadraCon { get; set; } = new List<string>();
        /// <summary>Lo que no cuadra con nada, con signo (negativo = pérdida de verdad, positivo = sobra de verdad).</summary>
        public int UnidadesSinCuadrar { get; set; }
        public decimal ValorSinCuadrar { get; set; }
        /// <summary>Tiene alguna alta y alguna baja en el año («aparece y desaparece»).</summary>
        public bool ApareceYDesaparece { get; set; }
    }

    /// <summary>
    /// NestoAPI#893: diarios de producto a mano (sin cliente, proveedor, pedido, albarán ni traspaso) que NO son inventario ni reposición.
    /// Falta decidir cuáles son pérdida desconocida y cuáles tienen causa (rotura, caducado, muestra, regalo): se enseñan aparte y no cuentan.
    /// </summary>
    public class DiarioSinClasificarDTO
    {
        public string Almacen { get; set; }
        public string Diario { get; set; }
        public int UnidadesAlta { get; set; }
        public int UnidadesBaja { get; set; }
        public int UnidadesNeto { get; set; }
        public decimal ValorNeto { get; set; }
        public int NumeroProductos { get; set; }
    }

    /// <summary>NestoAPI#893: GET api/Informes/PerdidaDesconocida (a coste, Dirección).</summary>
    public class PerdidaDesconocidaDTO
    {
        public int Anio { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<string> DiariosQueCuentan { get; set; } = new List<string>();
        public List<AlmacenPerdidaDesconocidaDTO> Almacenes { get; set; } = new List<AlmacenPerdidaDesconocidaDTO>();
        /// <summary>
        /// Pérdida en tránsito de una reposición en la que origen y destino no se ponen de acuerdo (#906), SIN imputar a ALG, REI ni
        /// ALC, mes a mes y acumulada. Hoy siempre a 0: no hay forma de distinguirla (ver <c>GestorPerdidaDesconocida.DIARIOS_EN_TRANSITO</c>).
        /// </summary>
        public List<PeriodoPerdidaDesconocidaDTO> EnTransito { get; set; } = new List<PeriodoPerdidaDesconocidaDTO>();
        public PeriodoPerdidaDesconocidaDTO EnTransitoAcumulado { get; set; }
        public PeriodoPerdidaDesconocidaDTO Total { get; set; }
        public List<ProductoPerdidaDesconocidaDTO> Productos { get; set; } = new List<ProductoPerdidaDesconocidaDTO>();
        public List<DiarioSinClasificarDTO> DiariosSinClasificar { get; set; } = new List<DiarioSinClasificarDTO>();
        public List<string> Notas { get; set; } = new List<string>();
    }
}
