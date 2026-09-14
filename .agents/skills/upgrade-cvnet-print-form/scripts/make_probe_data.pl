#!/usr/bin/perl
# 旧cvnet qfm のスロット→列 対応をPDF実描画で実測するためのプローブ用 data.txt を生成する。
#
# 使い方: perl make_probe_data.pl <itemの列数> [HEADの列数] > data.txt
#   例   : perl make_probe_data.pl 68        # 明細帳票（d_sql.txt が68列）
#
# 出力（cp932・CRLF）:
#   1行目 = H レコード。1列目 "H" + "H2".."H<n>" で、見出しがどの HEADn か分かる。
#   2,3行目 = データ行。"A01".."A<n>" で、値セルがどの itemN を出しているか分かる。
#           2行あるのは明細帳票の record 繰り返しを確認するため。
#
# 生成した data.txt を qfmprint ハーネスの workdir に置いて描画し、
# PDF のテキスト層を Read して旧 data.pdf の同じ位置と突き合わせる。
use strict;
use warnings;

my $items = shift // 40;
my $heads = shift // 91;
die "usage: make_probe_data.pl <item-count> [head-count]\n" unless $items =~ /^\d+$/ && $heads =~ /^\d+$/;

binmode(STDOUT, ':raw');

my @h = ('H');
push @h, "H$_" for (2 .. $heads);
my @i = map { sprintf('A%02d', $_) } (1 .. $items);

print join(',', @h), "\r\n";
print join(',', @i), "\r\n";
print join(',', @i), "\r\n";
