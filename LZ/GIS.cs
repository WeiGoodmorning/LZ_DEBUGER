using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace LZ
{

    public static class GIS
    {
        // 常量定义
        private const double PI = 3.14159265358979;
        private const double An = 6378137.0;
        private const double F1 = 1 / 298.257223563;
        private const double Rp = 6356752.3142452;
        private const double P = 0.0174532925199433; // pi/180

        private const double E12 = 0.006694379990141;
        private const double E11 = 0.081819190842622;
        private const double E22 = 0.00673949674227643;
        private const double E21 = 0.08209443794969570;

        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;
        private const double EarthRadius = 6371004;

        public static int Complanation(BlhPoint ORIPoint, BlhPoint INPoint, out XyzPoint OUTPoint)
        {
            XyzPoint ORIXYZPoint, OUTXYZPoint, OUTXYZPoint1;
            ORIXYZPoint = new XyzPoint();
            OUTXYZPoint = new XyzPoint();
            OUTXYZPoint1 = new XyzPoint();
            OUTPoint = new XyzPoint();

            GaussProDirect(ORIPoint, ORIPoint.Lon, out ORIXYZPoint); // 原点换算
            GaussProDirect(INPoint, ORIPoint.Lon, out OUTXYZPoint);  // 测试点换算
            OUTXYZPoint1.Y = OUTXYZPoint.Y - ORIXYZPoint.Y;
            OUTXYZPoint1.X = OUTXYZPoint.X - ORIXYZPoint.X;
            OUTXYZPoint1.Z = 0;
            // 旋转
            double angle;
            angle = ORIPoint.Heading + 360;
            double radian = (angle * PI) / 180.00; // 需要将角度转弧度
            OUTPoint.X = (OUTXYZPoint1.X) * Math.Cos(radian) + (OUTXYZPoint1.Y) * Math.Sin(radian);
            OUTPoint.Y = -1 * ((OUTXYZPoint1.Y) * Math.Cos(radian) - (OUTXYZPoint1.X) * Math.Sin(radian)); // （右手坐标系）
            return 0;
        }
        // 高斯投影正算（经纬度转XYZ）
        public static int GaussProDirect(BlhPoint blhPoint, double merLon, out XyzPoint xyzPoint)
        {
            xyzPoint = new XyzPoint();
            double lat = blhPoint.Lat * P;
            double lon = blhPoint.Lon * P - merLon * P;

            double rn = An / Math.Sqrt(1 - E12 * Math.Pow(Math.Sin(lat), 2));
            double lon2 = lon * lon;
            double lon4 = lon2 * lon2;
            double tnLat = Math.Tan(lat);
            double tn2Lat = tnLat * tnLat;
            double tn4Lat = tn2Lat * tn2Lat;
            double csLat = Math.Cos(lat);
            double cs2Lat = csLat * csLat;
            double cs4Lat = cs2Lat * cs2Lat;
            double eta2 = E22 * cs2Lat;
            double eta4 = eta2 * eta2;

            double ntblp = rn * tnLat * cs2Lat * lon2;
            double coe1 = (5 - tn2Lat + 9 * eta2 + 4 * eta4) * cs2Lat * lon2 / 24;
            double coe2 = (61 - 58 * tn2Lat + tn4Lat) * cs4Lat * lon4 / 720;
            double x = Merdian(lon, lat) + ntblp * (0.5 + coe1 + coe2);

            double nblp = rn * csLat * lon;
            double coe3 = (1 - tn2Lat + eta2) * cs2Lat * lon2 / 6;
            double coe4 = (5 - 18 * tn2Lat + tn4Lat + 14 * eta2 - 58 * tn2Lat * eta2) * cs4Lat * lon4 / 120;
            double y = nblp * (1 + coe3 + coe4) + 500000;

            xyzPoint.X = x;
            xyzPoint.Y = y;
            return 0;
        }

        // 高斯投影反算（XYZ转经纬度）
        public static int GaussProInverse(XyzPoint xyzPoint, double merLon, out BlhPoint blhPoint)
        {
            blhPoint = new BlhPoint();
            double x = xyzPoint.X;
            double y = xyzPoint.Y - 500000;

            double bf = Meridian2Latitude(x);
            double tnBf = Math.Tan(bf);
            double tn2Bf = tnBf * tnBf;
            double tn4Bf = tn2Bf * tn2Bf;
            double csBf = Math.Cos(bf);
            double cs2Bf = csBf * csBf;
            double eta2 = E22 * cs2Bf;
            double coe = Math.Sqrt(1 - E12 * Math.Pow(Math.Sin(bf), 2));
            double nf = An / coe;
            double mf = (An * (1 - E12)) / Math.Pow(coe, 3);

            // 计算纬度
            double ynf = y / nf;
            double ynf2 = ynf * ynf;
            double ynf4 = ynf2 * ynf2;
            double tymn = 0.5 * tnBf * y * ynf / mf;
            double coe1 = (5 + 3 * tn2Bf + eta2 - 9 * eta2 * tn2Bf) * ynf2 / 12;
            double coe2 = (61 + 90 * tn2Bf + 45 * tn4Bf) * ynf4 / 360;
            double lat = bf - tymn * (1 - coe1 + coe2);

            // 计算经度
            double ybnf = ynf / csBf;
            double coe3 = (1 + 2 * tn2Bf + eta2) * ynf2 / 6;
            double coe4 = (5 + 28 * tn2Bf + 24 * tn4Bf + 6 * eta2 + 8 * eta2 * tn2Bf) * ynf4 / 120;
            double lon = merLon * P + ybnf * (1 - coe3 + coe4);

            blhPoint.Lat = lat / P;
            blhPoint.Lon = lon / P;
            return 0;
        }

        // 坐标旋转
        public static void CoordinateRotation(XyzPoint xyzPoint1, double angle, out XyzPoint xyzPoint)
        {
            xyzPoint = new XyzPoint();
            double radian = angle * PI / 180;
            xyzPoint.X = xyzPoint1.X * Math.Cos(radian) + xyzPoint1.Y * Math.Sin(radian);
            xyzPoint.Y = xyzPoint1.Y * Math.Cos(radian) - xyzPoint1.X * Math.Sin(radian);
        }

        // 计算两点间距离
        public static double Point2Distance(Point1 p1, Point1 p2)
        {
            double a = Math.Sin((90 - p1.Lat) * Deg2Rad) * Math.Cos(p1.Lon * Deg2Rad);
            double b = Math.Sin((90 - p2.Lat) * Deg2Rad) * Math.Cos(p2.Lon * Deg2Rad);
            double c = Math.Pow(a - b, 2);

            a = Math.Sin((90 - p1.Lat) * Deg2Rad) * Math.Sin(p1.Lon * Deg2Rad);
            b = Math.Sin((90 - p2.Lat) * Deg2Rad) * Math.Sin(p2.Lon * Deg2Rad);
            double d = Math.Pow(a - b, 2);

            a = Math.Cos((90 - p1.Lat) * Deg2Rad);
            b = Math.Cos((90 - p2.Lat) * Deg2Rad);
            double e = Math.Pow(a - b, 2);

            double f = 1 - (c + d + e) / 2;
            f = Math.Clamp(f, -1, 1);
            double s = Math.Acos(f);
            return EarthRadius * s;
        }

        // 计算航向角
        public static double Point2Azimuth(Point1 p1, Point1 p2)
        {
            if (p1.Lon == p2.Lon)
            {
                return p2.Lat >= p1.Lat ? 0 : 180;
            }
            if (p1.Lat == p2.Lat)
            {
                return p2.Lon >= p1.Lon ? 90 : 270;
            }

            double latStart = p1.Lat * Deg2Rad;
            double lngStart = p1.Lon * Deg2Rad;
            double latEnd = p2.Lat * Deg2Rad;
            double lngEnd = p2.Lon * Deg2Rad;

            double tmpValue = Math.Sin(latStart) * Math.Sin(latEnd) +
                             Math.Cos(latStart) * Math.Cos(latEnd) * Math.Cos(lngEnd - lngStart);
            tmpValue = Math.Sqrt(1 - Math.Pow(tmpValue, 2));
            tmpValue = Math.Cos(latEnd) * Math.Sin(lngEnd - lngStart) / tmpValue;
            tmpValue = Math.Clamp(tmpValue, -1, 1);

            double resultAngle = Math.Abs(Math.Asin(tmpValue) * Rad2Deg);

            if (p2.Lon > p1.Lon)
            {
                return p2.Lat >= p1.Lat ? resultAngle : 180 - resultAngle;
            }
            else
            {
                return p2.Lat >= p1.Lat ? 360 - resultAngle : 180 + resultAngle;
            }
        }

        // 根据起点、距离和方位角计算终点
        public static void GetAnotherPoint(Point1 pA, double distance, double angle, out Point1 pB)
        {
            pB = new Point1();
            double b = Rp;
            double lon = pA.Lon;
            double lat = pA.Lat;

            double alpha1 = angle * Deg2Rad;
            double sinAlpha1 = Math.Sin(alpha1);
            double cosAlpha1 = Math.Cos(alpha1);

            double tanU1 = (1 - F1) * Math.Tan(lat * Deg2Rad);
            double cosU1 = 1 / Math.Sqrt(1 + tanU1 * tanU1);
            double sinU1 = tanU1 * cosU1;
            double sigma1 = Math.Atan2(tanU1, cosAlpha1);
            double sinAlpha = cosU1 * sinAlpha1;
            double cosSqAlpha = 1 - sinAlpha * sinAlpha;
            double uSq = cosSqAlpha * (An * An - b * b) / (b * b);

            double A = 1 + uSq / 16384 * (4096 + uSq * (-768 + uSq * (320 - 175 * uSq)));
            double B = uSq / 1024 * (256 + uSq * (-128 + uSq * (74 - 47 * uSq)));

            double sigma = distance / (b * A);
            double sigmaP = 2 * Math.PI;

            double cos2SigmaM = 0;
            double sinSigma = 0;
            double cosSigma = 0;

            while (Math.Abs(sigma - sigmaP) > 1e-12)
            {
                cos2SigmaM = Math.Cos(2 * sigma1 + sigma);
                sinSigma = Math.Sin(sigma);
                cosSigma = Math.Cos(sigma);
                double deltaSigma = B * sinSigma * (cos2SigmaM + B / 4 * (cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM) -
                                  B / 6 * cos2SigmaM * (-3 + 4 * sinSigma * sinSigma) * (-3 + 4 * cos2SigmaM * cos2SigmaM)));
                sigmaP = sigma;
                sigma = distance / (b * A) + deltaSigma;
            }

            double tmp = sinU1 * sinSigma - cosU1 * cosSigma * cosAlpha1;
            double lat2 = Math.Atan2(sinU1 * cosSigma + cosU1 * sinSigma * cosAlpha1,
                                    (1 - F1) * Math.Sqrt(sinAlpha * sinAlpha + tmp * tmp));
            double lambda = Math.Atan2(sinSigma * sinAlpha1, cosU1 * cosSigma - sinU1 * sinSigma * cosAlpha1);
            double C = F1 / 16 * cosSqAlpha * (4 + F1 * (4 - 3 * cosSqAlpha));
            double L = lambda - (1 - C) * F1 * sinAlpha * (sigma + C * sinSigma * (cos2SigmaM + C * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));

            pB.Lon = lon + Rad2Deg * L;
            pB.Lat = Rad2Deg * lat2;
        }

        // 子午线长度计算
        private static double Merdian(double eth, double lat)
        {
            double s0 = An * (1 - E12);
            double e2 = E12;
            double e4 = e2 * e2;
            double e6 = e4 * e2;
            double e8 = e6 * e2;
            double e10 = e8 * e2;
            double e12 = e10 * e2;

            double a1 = 1 + 3 * e2 / 4 + 45 * e4 / 64 + 175 * e6 / 256 + 11025 * e8 / 16384 + 43659 * e10 / 65536 + 693693 * e12 / 1048576;
            double b1 = 3 * e2 / 8 + 15 * e4 / 32 + 525 * e6 / 1024 + 2205 * e8 / 4096 + 72765 * e10 / 131072 + 297297 * e12 / 524288;
            double c1 = 15 * e4 / 256 + 105 * e6 / 1024 + 2205 * e8 / 16384 + 10395 * e10 / 65536 + 1486485 * e12 / 8388608;
            double d1 = 35 * e6 / 3072 + 105 * e8 / 4096 + 10395 * e10 / 262144 + 55055 * e12 / 1048576;
            double e1 = 315 * e8 / 131072 + 3465 * e10 / 524288 + 99099 * e12 / 8388608;
            double f1 = 693 * e10 / 1310720 + 9009 * e12 / 5242880;
            double g1 = 1001 * e12 / 8388608;

            return s0 * (a1 * lat - b1 * Math.Sin(2 * lat) + c1 * Math.Sin(4 * lat) - d1 * Math.Sin(6 * lat) +
                         e1 * Math.Sin(8 * lat) - f1 * Math.Sin(10 * lat) + g1 * Math.Sin(12 * lat));
        }

        // 由子午线弧长求纬度
        private static double Meridian2Latitude(double x1)
        {
            double m0 = An * (1 - E12);
            double m2 = 3 * E12 * m0 / 2;
            double m4 = 5 * E12 * m2 / 4;
            double m6 = 7 * E12 * m4 / 6;
            double m8 = 9 * E12 * m6 / 8;

            double a8 = m8 / 128;
            double a6 = m6 / 32 + m8 / 16;
            double a4 = m4 / 8 + 3 * m6 / 16 + 7 * m8 / 32;
            double a0 = m0 + m2 / 2 + 3 * m4 / 8 + 5 * m6 / 16 + 35 * m8 / 128;
            double a2 = m2 / 2 + m4 / 2 + 15 * m6 / 32 + 7 * m8 / 16;

            double b0 = x1 / a0;
            while (true)
            {
                double F = (-a2) * Math.Sin(2 * b0) / 2 + a4 * Math.Sin(4 * b0) / 4 -
                          a6 * Math.Sin(6 * b0) / 6 + a8 * Math.Sin(8 * b0) / 8;
                double bf = (x1 - F) / a0;
                if (Math.Abs(b0 - bf) < 1e-10)
                    break;
                b0 = bf;
            }
            return b0;
        }

    }
}
