// This work is licensed under the Creative Commons Attribution-NonCommercial-ShareAlike 3.0
// Unported License. To view a copy of this license, visit http://creativecommons.org/licenses/by-nc-sa/3.0/ 
// or send a letter to Creative Commons, PO Box 1866, Mountain View, CA 94042, USA.
// =========================================================================================================

vec3 red = vec3(255.,21.,47.)/255.;//vec3(.97,.12,.21);
vec3 yellow = vec3(255.,198.,80.)/255.;
vec3 pink = vec3(255.,159.,196.)/255.;

#define sat(x) clamp(x, 0., 1.)

float cir(vec2 p, float r) { return length(p)-r; }

vec3 patternBack(vec2 uv)
{
  vec3 col;
  float rep = 0.38;
  uv-=vec2(.1);
  float r = abs(cir(mod(uv,rep)-rep*.5,.14))-0.03;

  col = mix(col, red*.5*(1.-sat(uv.y+.3)),1.-sat(r*40.));
  return col;
}

vec4 spot(vec2 uv)
{
  float sz = 0.03;
  return vec4(pink*(1.-sat(cir(uv-vec2(-sz*.1),sz*.8)*40.)), cir(uv,sz));
}

vec3 post(vec2 uv,vec3 rd)
{
  vec3 col = rd;
  vec3 blue =vec3(89.,151.,255.)/255.;

  col += blue*(sin(uv.x+uv.y)*.5+.5);
  float an = uv.x+uv.y*.5;
  col += sat(uv.y)*blue*.5*sat((sin(an*25.+iTime)*.5+.5)+(sin(an*5.-iTime*.5)*.5+.5));

  float n = texture(iChannel0,vec2(sin(uv.y*5.+iTime+sin(uv.y*15.))*.05+uv.x,uv.y-iTime*.2)).x;
  col += blue*float(n>.85)*(sin(uv.x+uv.y)*.5+.5);
  return col;
}
vec3 rdr(vec2 uv, float shp)
{
  vec3 col = patternBack(uv);
  col = .2*post(uv*-1.,col);


vec2 uvt = vec2(abs(uv.x),uv.y*.7) -vec2(.38,-.2);
  float tent = abs(cir(uvt,.25))-.12;
  col = mix(col,red*(1.-sat(uvt.y)),1.-sat(tent*shp));

  float head = cir(uv-vec2(0.,.1), .4);
  col = mix(col,red,1.-sat(head*shp));

  vec2 uve = vec2(abs(uv.x),uv.y)-vec2(.25,0.);
  float eye = cir(uve,.17);
  col = mix(col,yellow,1.-sat(eye*shp));

  float eyeb = cir(uve,.12);
  col = mix(col,vec3(0.),1.-sat(eyeb*shp));

  vec2 uvew = vec2(abs(uv.x-.07),uv.y)-vec2(.25,0.07);
  float eyew = cir(uvew, .03);
  col = mix(col, vec3(1.),1.-sat(eyew*shp));

  vec2 uver = vec2(abs(uv.x-.15),uv.y)-vec2(.27,-0.15);
  float eyer = cir(uver,.15);
  col = mix(col,mix(col,vec3(1.),.3),1.-sat(max(eyer,eye)*shp));

  int i = 0;
  vec2 uvss = vec2(abs(uv.x),uv.y)-vec2(.37,0.);
  while (i<5)
  {
    float an = float(i-2)*.5;
    vec2 uvs = uvss+vec2(sin(an),cos(an)*2.)*.23;
    vec4 spt = spot(uvs);
    col = mix(col, spt.xyz,1.-sat(spt.w*shp));
    ++i;
  }
  return col;
}

void mainImage( out vec4 fragColor, in vec2 fragCoord ) {
  vec2 uv = (fragCoord.xy-.5*iResolution.xy) / iResolution.xx;
  uv*=2.;
  vec3 col = rdr(uv, mix(40.,400.,sin((uv.x+uv.y)*15.-iTime)*.5+.5));
  col = post(uv, col);
  fragColor = vec4(col, 1.0);
}