using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Peluqueria;
using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    internal static class ServicioSelectorTipoComisionesAnualesVendedor
    {

        public static IComisionesAnuales ComisionesVendedor(string vendedor, int anno, int mes)
        {
            using (var db = new NVEntities())
            {
                var vendedoresPeluqueria = db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_PELUQUERIA).Select(v => v.Número.Trim());

                if (vendedoresPeluqueria.Contains(vendedor))
                {
                    if (anno == 2018)
                    {
                        return new ComisionesAnualesPeluqueria2018();
                    }
                    else if (anno == 2019)
                    {
                        return new ComisionesAnualesPeluqueria2019();
                    }
                    else if (anno == 2020)
                    {
                        return new ComisionesAnualesPeluqueria2020();
                    }
                    else if (anno == 2021 || anno == 2022 || anno == 2023)
                    {
                        return new ComisionesAnualesPeluqueria2021(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2024)
                    {
                        return new ComisionesAnualesPeluqueria2024(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2025)
                    {
                        return new ComisionesAnualesPeluqueria2025(new ServicioComisionesAnualesComun());
                    }
                }

                var vendedoresTelefono = db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO).Select(v => v.Número.Trim());

                if (vendedoresTelefono.Contains(vendedor))
                {
                    if (anno == 2018)
                    {
                        return new ComisionesAnualesEstetica2018();
                    }
                    else if (anno == 2019)
                    {
                        return new ComisionesAnualesEstetica2019();
                    }
                    else if (anno == 2020)
                    {
                        return new ComisionesAnualesEstetica2020();
                    }
                    else if (anno == 2021)
                    {
                        return new ComisionesAnualesEstetica2021();
                    }
                    else if (anno == 2022 || anno == 2023)
                    {
                        return new ComisionesAnualesEstetica2022(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2024)
                    {
                        return new ComisionesAnualesTelefono2024(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2025 && vendedor == "PCN")
                    {
                        return new ComisionesAnnoParcial(new ComisionesAnualesTelefono2025(new ServicioComisionesAnualesComun()), 10);
                    }
                    else if (anno == 2025 && vendedor == "VCG")
                    {
                        return new ComisionesAnnoParcial(new ComisionesAnualesTelefono2025(new ServicioComisionesAnualesComun()), 8);
                    }
                    else if (anno == 2025 && vendedor == "LHY")
                    {
                        return new ComisionesAnnoParcial(new ComisionesAnualesTelefono2025(new ServicioComisionesAnualesComun()), 2);
                    }
                    else if (anno == 2025)
                    {
                        return new ComisionesAnualesTelefono2025(new ServicioComisionesAnualesComun());
                    }
                }

                var miniVendedores = db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_MINI).Select(v => v.Número.Trim());
                if (miniVendedores.Contains(vendedor))
                {
                    if (anno == 2018)
                    {
                        return new ComisionesAnualesEstetica2018();
                    }
                    else if (anno == 2019)
                    {
                        return new ComisionesAnualesEstetica2019();
                    }
                    else if (anno == 2020)
                    {
                        return new ComisionesAnualesEstetica2020();
                    }
                    else if (anno == 2021)
                    {
                        return new ComisionesAnualesEstetica2021();
                    }
                    else if (anno == 2022 || anno == 2023)
                    {
                        return new ComisionesAnualesEstetica2022(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2024)
                    {
                        return new ComisionesAnualesEstetica2024(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2025 && mes >= 9 && vendedor == "PI")
                    {
                        try
                        {
                            var comisionesCursos = new ComisionesAnualesCursos2025(new ServicioComisionesAnualesComun());
                            var comisionesTrimestre = new ComisionesAnnoParcial(comisionesCursos, 4);
                            return comisionesTrimestre;
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
                    else if (anno == 2025)
                    {
                        return new ComisionesAnualesMinivendedores2025(new ServicioComisionesAnualesComun());
                    }
                }

                var jefesVentas = db.EquiposVentas.Where(e => e.Superior == vendedor && e.FechaDesde <= new DateTime(anno, 1, 1)).Select(e => e.Superior).Distinct().ToList();

                if (jefesVentas.Contains(vendedor))
                {
                    if (anno == 2024)
                    {
                        return new ComisionesAnualesJefeVentas2024(new ServicioComisionesAnualesComun());
                    }
                    if (anno == 2025)
                    {
                        return new ComisionesAnualesJefeVentas2025(new ServicioComisionesAnualesComun());
                    }
                }

                if (anno == 2018)
                {
                    return new ComisionesAnualesEstetica2018();
                }
                else if (anno == 2019)
                {
                    return new ComisionesAnualesEstetica2019();
                }
                else if (anno == 2020)
                {
                    return new ComisionesAnualesEstetica2020();
                }
                else if (anno == 2021)
                {
                    return new ComisionesAnualesEstetica2021();
                }
                else if (anno == 2022 || anno == 2023)
                {
                    return new ComisionesAnualesEstetica2022(new ServicioComisionesAnualesComun());
                }
                else if (anno == 2024)
                {
                    return new ComisionesAnualesEstetica2024(new ServicioComisionesAnualesComun());
                }
                else if (anno == 2025 && vendedor != "ISR")
                {
                    return new ComisionesAnualesEstetica2025(new ServicioComisionesAnualesComun());
                }
                else if (anno == 2025 && vendedor == "ISR")
                {
                    return new ComisionesAnnoParcial(new ComisionesAnualesEstetica2025(new ServicioComisionesAnualesComun()), 2);
                }

                throw new Exception("El año " + anno.ToString() + " no está controlado por el sistema de comisiones");
            }
        }

        public static IEstrategiaComisionSobrepago EstrategiaComisionSobrepago(string vendedor, int anno, int mes)
        {
            return vendedor == "MRM" && anno == 2025 && mes >= 9
                ? new EstrategiaSobrepagoTramoAnterior()
                : (IEstrategiaComisionSobrepago)new EstrategiaSobrepagoDescuentoCompleto();
        }
    }
}